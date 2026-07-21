# João Sousa — Personal site

Static presentation page for a personal `.me` domain.

## Contents

- `index.html` — page structure
- `styles.css` — layout and visual identity
- `script.js` — scroll reveals, theme toggle, year stamp
- `images/hero-bg.png` — hero visual

## Preview locally

Open `index.html` in a browser, or from this folder:

```bash
npx --yes serve .
```

## Publish on a `.me` domain

Good fits: **Cloudflare Pages**, **Netlify**, or **GitHub Pages**.

1. Push this folder to a GitHub repo.
2. Connect the repo to Cloudflare Pages / Netlify (build command empty, publish directory `.`).
3. In the DNS of your `.me` domain, point to the host:
   - Cloudflare: add the domain in Pages and follow their CNAME/A records.
   - Netlify: add a custom domain and use the suggested DNS records.
4. Enable HTTPS (usually automatic).

Live site (GitHub Pages): https://jotasousa8.github.io/joao-sousa-dev/
