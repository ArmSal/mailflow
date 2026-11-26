using System.Net;
using System.Text;
using System.Text.Json;
using Mailflow.Agent;

namespace Mailflow.Integration.Tests.Agent;

public class PrintAgentTests
{
    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];
        public List<KeyValuePair<string, string>> Headers { get; } = [];
        public List<string> Urls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            foreach (var h in request.Headers) Headers.Add(new(h.Key, string.Join(",", h.Value)));
            Urls.Add(request.RequestUri!.PathAndQuery);
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private sealed class FakePrinter(Exception? fail = null) : IPdfPrinter
    {
        public List<(string Printer, int Copies, bool Duplex, bool FileExisted)> Printed { get; } = [];
        public string? LastPath { get; private set; }

        public Task PrintAsync(string pdfPath, string printerName, int copies, bool duplex, CancellationToken ct)
        {
            LastPath = pdfPath;
            Printed.Add((printerName, copies, duplex, File.Exists(pdfPath)));
            return fail is null ? Task.CompletedTask : Task.FromException(fail);
        }
    }

    private static HttpResponseMessage Json(object o) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };

    private static FakeServer ServerWithJob(HttpStatusCode pdfStatus = HttpStatusCode.OK) => new(req => req.RequestUri!.AbsolutePath switch
    {
        "/api/agent/jobs/next" => Json(new { id = 5, printerName = "HP", copies = 2, duplex = true, fileName = "a.pdf" }),
        "/api/agent/jobs/5/pdf" => new HttpResponseMessage(pdfStatus) { Content = new ByteArrayContent("%PDF-1.4"u8.ToArray()) },
        _ => new HttpResponseMessage(HttpStatusCode.OK),
    });

    private static AgentWorker Worker(FakeServer server, IPdfPrinter printer) =>
        new(new HttpClient(server) { BaseAddress = new Uri("http://mailflow.test/") }, printer, _ => { });

    [Fact]
    public async Task No_waiting_job_means_nothing_is_printed()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var printer = new FakePrinter();

        Assert.False(await Worker(server, printer).RunOnceAsync(default));
        Assert.Empty(printer.Printed);
        Assert.Single(server.Calls);
    }

    [Fact]
    public async Task The_agent_asks_mailflow_to_hold_the_request_until_a_job_exists()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Worker(server, new FakePrinter()).RunOnceAsync(default);

        Assert.Equal("/api/agent/jobs/next?wait=25", Assert.Single(server.Urls));
    }

    [Fact]
    public async Task Refused_password_is_reported_as_such()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<RejectedPasswordException>(() => Worker(server, new FakePrinter()).RunOnceAsync(default));
    }

    [Fact]
    public async Task Job_is_downloaded_printed_with_its_settings_reported_ok_and_the_temp_file_removed()
    {
        var server = ServerWithJob();
        var printer = new FakePrinter();

        Assert.True(await Worker(server, printer).RunOnceAsync(default));

        Assert.Equal(("HP", 2, true, true), Assert.Single(printer.Printed));
        Assert.False(File.Exists(printer.LastPath));
        var report = Assert.Single(server.Calls, c => c.Method == HttpMethod.Post);
        Assert.Equal("/api/agent/jobs/5/result", report.Path);
        using var body = JsonDocument.Parse(report.Body);
        Assert.True(body.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Print_failure_is_reported_with_its_message()
    {
        var server = ServerWithJob();
        var printer = new FakePrinter(new InvalidOperationException("paper jam"));

        Assert.True(await Worker(server, printer).RunOnceAsync(default));

        using var body = JsonDocument.Parse(Assert.Single(server.Calls, c => c.Method == HttpMethod.Post).Body);
        Assert.False(body.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("paper jam", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Unavailable_pdf_prints_nothing_and_reports_nothing()
    {
        var server = ServerWithJob(HttpStatusCode.NotFound);
        var printer = new FakePrinter();

        Assert.True(await Worker(server, printer).RunOnceAsync(default));

        Assert.Empty(printer.Printed);
        Assert.DoesNotContain(server.Calls, c => c.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("mailflow", false)]
    [InlineData("ftp://x", false)]
    [InlineData("http://100.64.0.5:5089", true)]
    [InlineData("https://mailflow.example.com", true)]
    public void Server_url_must_be_a_full_http_address(string url, bool ok)
    {
        var c = new AgentConfig { ServerUrl = url, AgentName = "Boutique", Password = "secret-secret" };
        Assert.Equal(ok, c.Validate() is null);
    }

    [Fact]
    public void Missing_password_is_invalid()
        => Assert.NotNull(new AgentConfig { ServerUrl = "http://x:1", AgentName = "Boutique" }.Validate());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void The_agent_needs_a_name(string name)
        => Assert.NotNull(new AgentConfig { ServerUrl = "http://x:1", AgentName = name, Password = "secret-secret" }.Validate());

    [Fact]
    public void Saved_password_round_trips_and_is_not_written_in_clear()
    {
        var path = Path.Combine(Path.GetTempPath(), "agent-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var c = new AgentConfig { ServerUrl = "http://x:1", AgentName = "Boutique" };
            c.SetPassword("a-long-secret-password");
            c.Save(path);

            Assert.DoesNotContain("a-long-secret-password", File.ReadAllText(path));
            var loaded = AgentConfig.Load(path)!;
            Assert.Equal("a-long-secret-password", loaded.ResolvedPassword);
            Assert.Null(loaded.Validate());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Every_request_carries_the_agent_name_and_password()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var config = new AgentConfig { ServerUrl = "http://mailflow.test:5089", AgentName = "Boutique Centre", Password = "secret-secret" };
        using var http = config.NewHttpClient(server);

        await http.GetAsync("api/agent/jobs/next");

        Assert.Equal("http://mailflow.test:5089/", http.BaseAddress!.ToString());
        Assert.Equal("Bearer secret-secret", server.Headers.Single(h => h.Key == "Authorization").Value);
        Assert.Equal("Boutique%20Centre", server.Headers.Single(h => h.Key == "X-Agent-Name").Value);
    }

    [Fact]
    public async Task An_accented_name_travels_url_encoded_and_decodes_back()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var http = new AgentConfig { ServerUrl = "http://x:1", AgentName = "Entrepôt Nord", Password = "secret-secret" }.NewHttpClient(server);

        await http.GetAsync("api/agent/ping");

        var sent = server.Headers.Single(h => h.Key == "X-Agent-Name").Value;
        Assert.DoesNotContain("ô", sent);
        Assert.Equal("Entrepôt Nord", Uri.UnescapeDataString(sent));
    }

    [Fact]
    public async Task The_printer_list_is_sent_to_mailflow()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var worker = Worker(server, new FakePrinter());

        await worker.ReportPrintersAsync(["HP LaserJet", "Brother"], default);

        var call = Assert.Single(server.Calls);
        Assert.Equal((HttpMethod.Post, "/api/agent/printers"), (call.Method, call.Path));
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal(["HP LaserJet", "Brother"], body.RootElement.GetProperty("names").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void Sumatra_arguments_follow_the_copies_and_duplex_choice()
        => Assert.Equal(["-print-to", "HP", "-silent", "-print-settings", "3x,duplexlong", "a.pdf"], SumatraPrinter.BuildArguments("a.pdf", "HP", 3, true));
}
