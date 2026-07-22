// extweigh.nephilim.jp のランディングページ配信 Worker。
// LP と付属静的素材だけを Worker が返し、Velopack / Store 配布物は同一ホスト名の
// R2 Custom Domain へ fetch(request) でそのまま委譲する。
import landingHtml from "./index.html";
import privacyHtml from "./privacy.html";
import styles from "./styles.css";
import script from "./script.js";
import robots from "./robots.txt";
import sitemap from "./sitemap.xml";

const securityHeaders = {
  "content-security-policy": "default-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; img-src 'self' data:; object-src 'none'; script-src 'self'; style-src 'self'",
  "referrer-policy": "strict-origin-when-cross-origin",
  "x-content-type-options": "nosniff",
  "x-frame-options": "DENY",
};

const staticAssets = new Map([
  ["/", { body: landingHtml, contentType: "text/html; charset=utf-8", cache: "public, max-age=300" }],
  ["/index.html", { body: landingHtml, contentType: "text/html; charset=utf-8", cache: "public, max-age=300" }],
  ["/privacy", { body: privacyHtml, contentType: "text/html; charset=utf-8", cache: "public, max-age=300" }],
  ["/privacy.html", { body: privacyHtml, contentType: "text/html; charset=utf-8", cache: "public, max-age=300" }],
  ["/styles.css", { body: styles, contentType: "text/css; charset=utf-8", cache: "public, max-age=86400" }],
  ["/script.js", { body: script, contentType: "text/javascript; charset=utf-8", cache: "public, max-age=86400" }],
  ["/robots.txt", { body: robots, contentType: "text/plain; charset=utf-8", cache: "public, max-age=86400" }],
  ["/sitemap.xml", { body: sitemap, contentType: "application/xml; charset=utf-8", cache: "public, max-age=86400" }],
]);

export default {
  async fetch(request) {
    const url = new URL(request.url);
    const asset = staticAssets.get(url.pathname);

    if (asset && (request.method === "GET" || request.method === "HEAD")) {
      return new Response(request.method === "HEAD" ? null : asset.body, {
        headers: {
          ...securityHeaders,
          "cache-control": asset.cache,
          "content-type": asset.contentType,
        },
      });
    }

    // 更新 package、固定 Setup / Portable、Store 用バージョン固定 Setup は R2 が配信する。
    return fetch(request);
  },
};
