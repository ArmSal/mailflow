# Mailflow design system

Reference for restyling pages. Everything is local (no CDN, no web fonts): Bootstrap 5.3 stays loaded, and our CSS bridges it to the theme so legacy Bootstrap markup keeps working in light and dark. Prefer the kit classes below for new markup.

Files (`src/Mailflow.Web/wwwroot`), loaded by `Pages/Shared/_Layout.cshtml` in this order:

| File | Role |
|---|---|
| `css/tokens.css` | Design tokens (CSS variables), light + dark |
| `css/base.css` | Typography, focus ring, Bootstrap bridge (`--bs-*` mapped to tokens), `.icon` |
| `css/layout.css` | App shell: side navigation, top bar, auth shell |
| `css/components.css` | Components (this document) |
| `js/theme.js` | Sets `data-theme` before first paint (saved choice, else OS preference) |
| `js/site.js` | Theme toggle, mobile nav drawer, toast auto-hide |

Never hard-code a colour: use a token. Never add a second stylesheet per page: extend `components.css` only if a component is reused by 2+ pages.

## Tokens

Light is the default, dark applies from `prefers-color-scheme` or `data-theme="dark"`. Use only `var(--x)`.

| Token | Use |
|---|---|
| `--bg`, `--surface`, `--surface-2` | page background, cards/tables/inputs, table header and hover fills |
| `--text`, `--text-muted` | body text, secondary text (both >= 4.5:1 on `--surface` and `--bg`) |
| `--border`, `--border-strong`, `--input-border` | hairlines, hover borders, form control borders (3:1) |
| `--accent`, `--accent-hover`, `--accent-soft`, `--on-accent` | petrol teal: primary buttons, links, icons; soft tint; text on accent |
| `--ok` / `--ok-bg`, `--warn` / `--warn-bg`, `--error` / `--error-bg`, `--neutral` / `--neutral-bg` | status text colour and its tinted background |
| `--focus` | focus ring colour |
| `--fs-xs/sm/base/lg/xl/2xl` | type scale (.75 / .8125 / .9375 / 1.0625 / 1.375 / 1.75 rem) |
| `--sp-1..6` | spacing (.25 / .5 / .75 / 1 / 1.5 / 2 rem) |
| `--radius-sm/--radius/--radius-lg` | 6 / 10 / 14 px |

Font: system stack (`Segoe UI Variable`, `Segoe UI`, `system-ui`). Numbers in tiles/tables use `font-variant-numeric: tabular-nums` (`.num`, `.stat-value` do it).

## Shell (already in `_Layout.cshtml`, do not repeat in pages)

Authenticated pages render inside `.app-shell` (side navigation `.sidenav` with groups, top bar `.topbar` with the worker pill `#worker-pill`, theme toggle, logout) and a `<main class="page">`. Anonymous pages (Login, Setup, Error) render inside `.auth-shell > .auth-card`. A page only writes its own content. Under 900 px the navigation becomes a drawer.

Menu active state is automatic (most specific URL wins). A new top-level page needs one line in the `groups` array of `_Layout.cshtml`.

## Icons

Inline sprite in `Pages/Shared/_Icons.cshtml`, 24x24 stroke icons. Always decorative unless alone in a button (then put `aria-label` on the button).

```html
<svg class="icon" aria-hidden="true"><use href="#i-store" /></svg>
```

Available: logo home store inbox printer tag rules shield clock list sliders logout menu sun moon play pause refresh file alert check plus lock. Add a `<symbol id="i-name" viewBox="0 0 24 24">` with `path`s (no fill, the CSS strokes them) in `_Icons.cshtml` if you need another.

## Components

### Page header

```html
<div class="page-header">
  <div><h1 class="page-title">Magasins</h1><p class="page-subtitle">Les points de vente qui reçoivent des documents.</p></div>
  <div class="page-actions"><a class="btn btn-primary" asp-page="Edit"><svg class="icon" aria-hidden="true"><use href="#i-plus" /></svg>Ajouter un magasin</a></div>
</div>
```

One `h1` per page (`.page-title`). `.section-title` is the `h2` above a block. `.stack` = vertical rhythm between blocks; `.split` = two columns >= 1300 px.

### Card

```html
<section class="card">
  <div class="card-header"><h2 class="card-title">Titre</h2><a class="btn btn-sm btn-secondary" href="#">Action</a></div>
  <div class="card-body">Contenu</div>
  <div class="card-footer">Pied</div>
</section>
```

A `.table-wrap` placed directly in a `.card` loses its own border.

### Stat tiles

```html
<div class="stat-grid">
  <div class="stat"><div><div class="stat-label">Messages traités</div><div class="stat-value">60</div></div>
    <span class="stat-icon"><svg class="icon" aria-hidden="true"><use href="#i-inbox" /></svg></span></div>
  <div class="stat stat--error">...</div> <!-- red variant -->
</div>
```

### Table

Always wrap in `.table-wrap` (rounded border, horizontal scroll on narrow widths). Header cells need `scope="col"`.

```html
<div class="table-wrap">           <!-- add .table-sticky for a scrolling body with sticky header -->
  <table class="table align-middle">
    <thead><tr><th scope="col">Nom</th><th scope="col" class="num">Total</th><th scope="col"></th></tr></thead>
    <tbody>
      <tr>
        <td>Store A<span class="cell-sub">1 Example Street</span></td>
        <td class="num">12</td>
        <td><div class="row-actions"><a class="btn btn-sm btn-secondary" href="#">Modifier</a>
            <form method="post" hx-post="..." hx-target="closest tr" hx-swap="outerHTML" hx-confirm="Supprimer ce magasin ?"><button class="btn btn-sm btn-danger" type="submit">Supprimer</button></form></div></td>
      </tr>
    </tbody>
  </table>
</div>
```

Helpers: `.cell-sub` (muted second line), `.cell-clip` (single line with ellipsis, put `title` on it), `.num` (right-aligned numbers), `.row-actions` (right-aligned buttons/forms), `.cell-result` (min width for message columns). Rows have hover; no zebra.

### Badges

```html
<span class="badge badge-ok">Actif</span> <span class="badge badge-warn">En pause</span>
<span class="badge badge-error">Erreur</span> <span class="badge badge-neutral">Inactif</span>
```

Colour + dot + text, never colour alone. A plain `.badge` with an inline `background-color` (category colour) keeps white text: only use dark-enough colours.

### Buttons

```html
<button class="btn btn-primary">Enregistrer</button>   <!-- one primary per view -->
<button class="btn btn-secondary">Annuler</button>
<button class="btn btn-danger btn-sm">Supprimer</button>
<button class="btn btn-ghost">Voir plus</button>
<button class="btn btn-ghost btn-icon" aria-label="Menu"><svg class="icon" aria-hidden="true"><use href="#i-menu" /></svg></button>
```

Sizes: `btn-sm`, default, `btn-lg`. Legacy `btn-outline-secondary/primary/warning` render as secondary, `btn-outline-danger` as danger, `btn-success` as a soft green, `btn-link` as an accent link. Loading state is automatic: htmx adds `.htmx-request` to the `<form>` (or the button when it carries the `hx-*`), which shows a spinner and blocks clicks. Do not add your own spinner.

### Forms

```html
<form method="post">
  <section class="form-section">
    <h2 class="form-section-title">Identité</h2>
    <p class="form-section-hint">Ces informations apparaissent dans la liste.</p>
    <div class="form-grid">
      <div class="field"><label class="form-label" asp-for="Input.Name">Nom</label>
        <input class="form-control" asp-for="Input.Name" />
        <span class="form-text">Visible dans le tableau de bord.</span>
        <span class="field-validation-error" asp-validation-for="Input.Name"></span></div>
      <div class="field"><label class="form-label" asp-for="Input.Type">Type</label><select class="form-select" asp-for="Input.Type">...</select></div>
    </div>
    <div class="form-check form-switch mb-3"><input class="form-check-input" type="checkbox" role="switch" asp-for="Input.IsActive" /><label class="form-check-label" asp-for="Input.IsActive">Actif</label></div>
  </section>
  <div class="form-actions"><button class="btn btn-primary" type="submit">Enregistrer</button><a class="btn btn-ghost" asp-page="Index">Annuler</a></div>
</form>
```

Keep `asp-for` names untouched. `.form-grid` = responsive columns; a lone `.field` is full width. Error summary: `<div asp-validation-summary="ModelOnly" class="alert alert-danger"></div>`. Toggles are `form-check form-switch` (checkbox with `role="switch"`).

### Toolbar (filters)

```html
<form method="get" class="toolbar" role="search" aria-label="Filtrer">
  <div class="field"><label class="form-label" for="X">Boîte mail</label><select class="form-select" id="X" name="X">...</select></div>
  <div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" id="Y" name="Y" value="true" /><label class="form-check-label" for="Y">Erreurs seulement</label></div>
  <div class="toolbar-end"><button class="btn btn-primary" type="submit">Filtrer</button></div>
</form>
```

### Pagination

Bootstrap markup, themed: `<nav aria-label="Pagination"><ul class="pagination"><li class="page-item disabled"><a class="page-link" ...>Précédent</a></li>...</ul></nav>` (see `Pages/Journal/Index.cshtml`).

### Alerts and toasts

```html
<div class="alert alert-success" role="status">Enregistré.</div>   <!-- alert-danger | alert-warning | alert-info -->
```

Toasts: the layout has an empty `<div id="toasts" class="toast-region">`. To show one from htmx, return an element with `hx-swap-oob="beforeend:#toasts"` containing `<div class="alert alert-success" data-autohide>Enregistré.</div>`; it disappears after 6 s. Use inline `.alert` for form results and toasts for confirmations after row actions.

### Tabs

```html
<nav class="tabs" aria-label="Sections"><a class="tab" href="#" aria-current="page">Général</a><a class="tab" href="#">Sauvegarde</a></nav>
```

### Empty state

Use instead of an empty table. One sentence, one call to action when something can be created.

```html
<div class="card"><div class="empty-state">
  <span class="empty-state-icon"><svg class="icon" aria-hidden="true"><use href="#i-store" /></svg></span>
  <h2 class="empty-state-title">Aucun magasin</h2>
  <p>Ajoutez votre premier magasin pour y rattacher des boîtes mail.</p>
  <a class="btn btn-primary" asp-page="Edit">Ajouter un magasin</a>
</div></div>
```

### Worker status

`<partial name="_WorkerPill" model="Worker" />` is already in the top bar. On the dashboard, `.status-panel` (`data-state="idle|running|paused"`) shows the large state; see `_StatusBlock.cshtml`.

### Event list

`<ul class="event-list"><li><svg class="icon" aria-hidden="true"><use href="#i-alert" /></svg><div><span class="event-title">Nom</span> <span class="event-meta">date</span><div class="event-text">message</div></div></li></ul>` inside a `.card`.

## Page skeleton

```html
@page
@model ...
@{ ViewData["Title"] = "Magasins"; }
<div class="page-header"> ... </div>
<!-- optional: <form class="toolbar"> ... </form> -->
<!-- either the table or the empty state -->
@if (Model.Items.Count == 0) { <div class="card"><div class="empty-state">...</div></div> }
else { <div class="table-wrap"><table class="table align-middle">...</table></div> }
```

The page `<title>` comes from `ViewData["Title"]` (French, no product name: the layout appends " - Mailflow").

## htmx conventions

- Keep ids, `hx-post`/`hx-get` URLs, `hx-target` and `hx-swap` exactly as in the PageModel handlers. Row actions: `hx-target="closest tr" hx-swap="outerHTML"`; whole blocks: `hx-target="#status" hx-swap="outerHTML"`.
- Put `hx-*` on the `<form>` (not only the button): loading styling keys off `form.htmx-request`.
- Polling blocks (`hx-trigger="every 10s"`) must not show a spinner on every poll: the style only targets forms/buttons, so it is safe.
- A fragment returned to htmx must be a partial (no layout). When it needs to update the top-bar pill, render `_WorkerPill` with `ViewData["Oob"] = true` only for htmx requests (see `_StatusBlock.cshtml`).
- Confirm destructive actions with `hx-confirm="..."` in French.

## Wording (French)

Sentence case, accents everywhere (Boîtes mail, Catégories, Règles, Expéditeurs autorisés, Paramètres). Buttons name the action: Enregistrer, Ajouter un magasin, Supprimer, Annuler, Filtrer, Lancer maintenant. The same action keeps the same name everywhere. Errors say what happened and what to do. Empty states invite an action. Dates `dd/MM/yyyy HH:mm:ss`.

## Do / Don't

Do
- Use tokens and kit classes; keep Bootstrap grid utilities (`row`, `col-*`, `d-flex`, `gap-*`, `mb-*`, `text-nowrap`) for spacing only.
- Give every icon-only button an `aria-label`; keep labels linked (`for`/`asp-for`).
- Show status with badge text, not colour alone. Keep `scope="col"` on headers.
- Test in light and dark, at 1000 px and 1280 px wide.

Don't
- Don't hard-code colours, shadows or font sizes; don't use `style=""` except for dynamic data colours.
- Don't add CDN links, web fonts, icon fonts or JS frameworks.
- Don't wrap a table without `.table-wrap`, or stack several primary buttons in one view.
- Don't use `text-white`, `bg-light`, `bg-dark`, `table-light` or other fixed-colour Bootstrap utilities (they break in dark mode); `text-muted`, `text-danger`, `text-success` are themed and fine for small notes.
- Don't change element ids, `name` attributes or handler routes: PageModels and tests depend on them.
