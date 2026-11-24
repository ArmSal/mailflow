var app = await Mailflow.Web.MailflowHost.StartAsync(args, CancellationToken.None);
await app.WaitForShutdownAsync();
