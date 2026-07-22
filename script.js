const yearEl = document.getElementById("year");
if (yearEl) {
  yearEl.textContent = String(new Date().getFullYear());
}

const header = document.querySelector(".site-header");
const backToTop = document.getElementById("back-to-top");
const pages = document.querySelectorAll("[data-page]");
const navLinks = document.querySelectorAll("[data-nav]");

const pageTitles = {
  home: "João Sousa — Senior Software Engineer",
  work: "Work — João Sousa",
  cases: "Case studies — João Sousa",
  "case-defined": "Defined.ai case study — João Sousa",
  "case-farfetch": "Farfetch case study — João Sousa",
  projects: "Projects — João Sousa",
  stack: "Stack — João Sousa",
  more: "Beyond work — João Sousa",
  admin: "Analytics — João Sousa",
  contact: "Contact — João Sousa",
};

const routeMap = {
  "": "home",
  home: "home",
  top: "home",
  work: "work",
  cases: "cases",
  "cases/defined": "case-defined",
  "cases/farfetch": "case-farfetch",
  projects: "projects",
  stack: "stack",
  more: "more",
  admin: "admin",
  contact: "contact",
};

const hashForRoute = {
  home: "#/",
  work: "#/work",
  cases: "#/cases",
  "case-defined": "#/cases/defined",
  "case-farfetch": "#/cases/farfetch",
  projects: "#/projects",
  stack: "#/stack",
  more: "#/more",
  admin: "#/admin",
  contact: "#/contact",
};

const navForRoute = {
  home: "home",
  work: "work",
  cases: "cases",
  "case-defined": "cases",
  "case-farfetch": "cases",
  projects: "projects",
  stack: "stack",
  more: "more",
  admin: "home",
  contact: "contact",
};

const normalizeRoute = (hash) => {
  const raw = (hash || "").replace(/^#\/?/, "").trim().toLowerCase();
  return routeMap[raw] || "home";
};

const analyticsMeta = document.querySelector('meta[name="analytics-endpoint"]');
// On localhost, always hit the local API — ignore production meta so local-dev-key works.
const analyticsEndpoint = (
  ["localhost", "127.0.0.1"].includes(location.hostname)
    ? "http://localhost:5095"
    : analyticsMeta?.getAttribute("content") || ""
).replace(/\/$/, "");

const pathForRoute = (route) => {
  const hash = hashForRoute[route] || "#/";
  return hash === "#/" ? "/" : hash.replace(/^#/, "") || "/";
};

const readQueryParams = () => {
  const params = new URLSearchParams(location.search);
  const hash = location.hash || "";
  const qIndex = hash.indexOf("?");
  if (qIndex >= 0) {
    const hashParams = new URLSearchParams(hash.slice(qIndex + 1));
    hashParams.forEach((value, key) => {
      if (!params.has(key)) params.set(key, value);
    });
  }
  return params;
};

const trackPageView = (route) => {
  if (!analyticsEndpoint || route === "admin") return;
  const params = readQueryParams();
  const payload = JSON.stringify({
    path: pathForRoute(route),
    referrer: document.referrer || "",
    language: navigator.language || "",
    screenWidth: window.screen?.width || 0,
    screenHeight: window.screen?.height || 0,
    utmSource: params.get("utm_source") || "",
    utmMedium: params.get("utm_medium") || "",
    utmCampaign: params.get("utm_campaign") || "",
    utmContent: params.get("utm_content") || "",
    utmTerm: params.get("utm_term") || "",
  });
  fetch(`${analyticsEndpoint}/api/analytics/pageview`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: payload,
    keepalive: true,
    mode: "cors",
  }).catch(() => {});
};

const showPage = (route) => {
  pages.forEach((page) => {
    const match = page.dataset.page === route;
    page.classList.toggle("is-active", match);
    page.hidden = !match;
  });

  const activeNav = navForRoute[route] || "home";
  navLinks.forEach((link) => {
    link.classList.toggle("is-active", link.dataset.nav === activeNav);
  });

  document.title = pageTitles[route] || pageTitles.home;
  window.scrollTo({ top: 0, behavior: "auto" });
  onScroll();
  trackPageView(route);
};

const navigate = (route, replace = false) => {
  const nextHash = hashForRoute[route] || "#/";
  if (replace) {
    history.replaceState(null, "", nextHash);
  } else if (location.hash !== nextHash) {
    location.hash = nextHash;
    return;
  }
  showPage(route);
};

const syncFromHash = () => {
  showPage(normalizeRoute(location.hash));
};

document.addEventListener("click", (event) => {
  const link = event.target.closest("[data-link]");
  if (!link) return;
  const href = link.getAttribute("href") || "";
  if (!href.startsWith("#")) return;
  event.preventDefault();
  navigate(normalizeRoute(href));
});

window.addEventListener("hashchange", syncFromHash);

const onScroll = () => {
  const y = window.scrollY;
  if (header) {
    header.classList.toggle("is-scrolled", y > 8);
  }
  if (backToTop) {
    const show = y > 320;
    backToTop.classList.toggle("is-visible", show);
    backToTop.hidden = !show;
  }
};

onScroll();
window.addEventListener("scroll", onScroll, { passive: true });

if (backToTop) {
  backToTop.addEventListener("click", () => {
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    window.scrollTo({ top: 0, behavior: reduceMotion ? "auto" : "smooth" });
  });
}

const root = document.documentElement;
const themeToggle = document.getElementById("theme-toggle");

const getTheme = () =>
  root.getAttribute("data-theme") === "dark" ? "dark" : "light";

const setTheme = (theme) => {
  root.setAttribute("data-theme", theme);
  localStorage.setItem("theme", theme);
  const meta = document.querySelector('meta[name="theme-color"]');
  if (meta) {
    meta.setAttribute("content", theme === "dark" ? "#071412" : "#0f9d8a");
  }
  if (themeToggle) {
    themeToggle.setAttribute(
      "aria-label",
      theme === "dark" ? "Switch to light mode" : "Switch to dark mode"
    );
  }
};

if (themeToggle) {
  themeToggle.addEventListener("click", () => {
    setTheme(getTheme() === "dark" ? "light" : "dark");
  });
  setTheme(getTheme());
}

const roles = [
  "cloud-native backends",
  "APIs that stay fast",
  "data platforms",
  "systems under pressure",
];
const typedEl = document.getElementById("typed-role");
const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

if (typedEl && !reduceMotion) {
  let roleIndex = 0;
  let charIndex = roles[0].length;
  let deleting = true;

  const tick = () => {
    const current = roles[roleIndex];
    if (!deleting && charIndex <= current.length) {
      typedEl.textContent = current.slice(0, charIndex);
      charIndex += 1;
      if (charIndex > current.length) {
        deleting = true;
        window.setTimeout(tick, 1600);
        return;
      }
      window.setTimeout(tick, 70);
      return;
    }

    if (deleting && charIndex >= 0) {
      typedEl.textContent = current.slice(0, charIndex);
      charIndex -= 1;
      if (charIndex < 0) {
        deleting = false;
        roleIndex = (roleIndex + 1) % roles.length;
        charIndex = 0;
        window.setTimeout(tick, 280);
        return;
      }
      window.setTimeout(tick, 36);
    }
  };

  window.setTimeout(tick, 1400);
}

if (!location.hash || location.hash === "#") {
  navigate("home", true);
} else {
  syncFromHash();
}

const ADMIN_KEY_STORAGE = "analyticsApiKey";
const adminForm = document.getElementById("admin-form");
const adminKeyInput = document.getElementById("admin-api-key");
const adminError = document.getElementById("admin-error");
const adminStats = document.getElementById("admin-stats");
const adminClear = document.getElementById("admin-clear");
const adminHint = document.getElementById("admin-endpoint-hint");

if (adminHint) {
  // Only show the endpoint locally — never expose the production API URL in the UI.
  const isLocal = ["localhost", "127.0.0.1"].includes(location.hostname);
  if (isLocal && analyticsEndpoint) {
    adminHint.hidden = false;
    adminHint.textContent = `API: ${analyticsEndpoint}`;
  } else {
    adminHint.hidden = true;
  }
}

if (adminKeyInput) {
  const saved = localStorage.getItem(ADMIN_KEY_STORAGE);
  if (saved) {
    adminKeyInput.value = saved;
    if (adminClear) adminClear.hidden = false;
  }
}

const fillTable = (tableId, rows, columns) => {
  const tbody = document.querySelector(`#${tableId} tbody`);
  if (!tbody) return;
  tbody.replaceChildren();
  rows.forEach((row) => {
    const tr = document.createElement("tr");
    columns.forEach((col) => {
      const td = document.createElement("td");
      const value = typeof col === "function" ? col(row) : row[col];
      td.textContent = value == null || value === "" ? "—" : String(value);
      tr.appendChild(td);
    });
    tbody.appendChild(tr);
  });
};

const formatLisbonTime = (iso) => {
  if (!iso) return "—";
  try {
    return new Intl.DateTimeFormat("pt-PT", {
      timeZone: "Europe/Lisbon",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hour12: false,
    }).format(new Date(iso));
  } catch {
    return "—";
  }
};

const formatUtm = (row) => {
  const parts = [
    row.utmSource && `src=${row.utmSource}`,
    row.utmMedium && `med=${row.utmMedium}`,
    row.utmCampaign && `camp=${row.utmCampaign}`,
    row.utmContent && `cnt=${row.utmContent}`,
    row.utmTerm && `term=${row.utmTerm}`,
  ].filter(Boolean);
  return parts.length ? parts.join(" · ") : "—";
};

const toDatetimeLocalValue = (date) => {
  const pad = (n) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
};

const setRangePreset = (days) => {
  const to = new Date();
  const from = new Date(to.getTime() - days * 24 * 60 * 60 * 1000);
  const fromInput = document.getElementById("admin-from");
  const toInput = document.getElementById("admin-to");
  if (fromInput) fromInput.value = toDatetimeLocalValue(from);
  if (toInput) toInput.value = toDatetimeLocalValue(to);
};

const renderAdminStats = (data) => {
  const set = (id, value) => {
    const el = document.getElementById(id);
    if (el) el.textContent = String(value ?? "—");
  };
  set("kpi-total", data.totalViews);
  set("kpi-7", data.viewsLast7Days);
  set("kpi-30", data.viewsLast30Days);
  set("kpi-unique", data.uniqueVisitorsLast30Days);

  const tzNote = document.getElementById("admin-tz-note");
  if (tzNote) {
    tzNote.textContent =
      "Horário: Europe/Lisbon (Portugal). Os eventos são guardados em UTC e convertidos aqui.";
  }

  const rangeHint = document.getElementById("admin-range-hint");
  if (rangeHint && data.range) {
    rangeHint.textContent = `Range: ${formatLisbonTime(data.range.fromUtc)} → ${formatLisbonTime(data.range.toUtc)} · matched ${data.range.matched}, showing ${data.range.returned}`;
  }

  fillTable("admin-by-day", data.byDay || [], ["day", "views"]);
  fillTable("admin-by-ip", data.byIp || [], [
    "ip",
    "views",
    (row) => row.daysActive ?? (row.days || []).length ?? "—",
    (row) => row.country || "—",
    (row) => formatLisbonTime(row.lastSeenUtc),
  ]);
  fillTable("admin-by-path", data.byPath || [], ["path", "views"]);
  fillTable("admin-by-country", data.byCountry || [], ["country", "views"]);
  fillTable("admin-by-browser", data.byBrowser || [], ["browser", "views"]);
  fillTable("admin-by-os", data.byOs || [], ["os", "views"]);
  fillTable("admin-by-language", data.byLanguage || [], ["language", "views"]);
  fillTable("admin-by-utm", data.byUtmSource || [], ["source", "views"]);
  fillTable("admin-recent", data.recent || [], [
    (row) => formatLisbonTime(row.occurredAtUtc),
    (row) => row.ip || "—",
    "path",
    (row) => row.country || "—",
    (row) => [row.browser, row.os, row.screen].filter(Boolean).join(" · ") || "—",
    (row) => {
      const utm = formatUtm(row);
      if (utm !== "—") return utm;
      return row.referrer || "—";
    },
  ]);

  if (adminStats) adminStats.hidden = false;
};

const loadAdminStats = async (apiKey) => {
  if (!analyticsEndpoint) {
    if (adminError) {
      adminError.hidden = false;
      adminError.textContent = "Analytics API endpoint is not configured.";
    }
    return;
  }

  if (adminError) adminError.hidden = true;

  const params = new URLSearchParams();
  const fromInput = document.getElementById("admin-from");
  const toInput = document.getElementById("admin-to");
  if (fromInput?.value) params.set("from", fromInput.value);
  if (toInput?.value) params.set("to", toInput.value);
  params.set("limit", "200");

  const qs = params.toString();
  const response = await fetch(
    `${analyticsEndpoint}/api/analytics/summary${qs ? `?${qs}` : ""}`,
    {
      headers: { "X-Api-Key": apiKey },
      mode: "cors",
    }
  );

  if (!response.ok) {
    if (adminError) {
      adminError.hidden = false;
      adminError.textContent =
        response.status === 401
          ? "Invalid API key."
          : `Could not load stats (${response.status}).`;
    }
    if (adminStats) adminStats.hidden = true;
    return;
  }

  const data = await response.json();
  localStorage.setItem(ADMIN_KEY_STORAGE, apiKey);
  if (adminClear) adminClear.hidden = false;
  renderAdminStats(data);
};

if (adminForm && adminKeyInput) {
  adminForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const key = adminKeyInput.value.trim();
    if (!key) {
      if (adminError) {
        adminError.hidden = false;
        adminError.textContent = "Enter the API key.";
      }
      return;
    }
    if (!document.getElementById("admin-from")?.value) {
      setRangePreset(7);
    }
    loadAdminStats(key).catch(() => {
      if (adminError) {
        adminError.hidden = false;
        adminError.textContent = "Network error talking to the analytics API.";
      }
    });
  });
}

const adminRangeForm = document.getElementById("admin-range-form");
if (adminRangeForm && adminKeyInput) {
  adminRangeForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const key = adminKeyInput.value.trim();
    if (!key) {
      if (adminError) {
        adminError.hidden = false;
        adminError.textContent = "Enter the API key.";
      }
      return;
    }
    loadAdminStats(key).catch(() => {
      if (adminError) {
        adminError.hidden = false;
        adminError.textContent = "Network error talking to the analytics API.";
      }
    });
  });

  adminRangeForm.querySelectorAll("[data-range]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const days = Number(btn.getAttribute("data-range") || "7");
      setRangePreset(days);
      const key = adminKeyInput.value.trim();
      if (key) {
        loadAdminStats(key).catch(() => {});
      }
    });
  });
}

if (adminClear && adminKeyInput) {
  adminClear.addEventListener("click", () => {
    localStorage.removeItem(ADMIN_KEY_STORAGE);
    adminKeyInput.value = "";
    adminClear.hidden = true;
    if (adminStats) adminStats.hidden = true;
    if (adminError) adminError.hidden = true;
  });
}

const adminSqlForm = document.getElementById("admin-sql-form");
const adminSqlInput = document.getElementById("admin-sql");
const adminSqlHint = document.getElementById("admin-sql-hint");
const adminSqlError = document.getElementById("admin-sql-error");
const adminSqlSchemaBtn = document.getElementById("admin-sql-schema");
const adminSchema = document.getElementById("admin-schema");

const requireAdminKey = () => {
  const key = adminKeyInput?.value.trim() || "";
  if (!key) {
    if (adminError) {
      adminError.hidden = false;
      adminError.textContent = "Enter the API key.";
    }
    return null;
  }
  return key;
};

const renderSqlResult = (data) => {
  const table = document.getElementById("admin-sql-result");
  if (!table) return;
  const thead = table.querySelector("thead");
  const tbody = table.querySelector("tbody");
  if (!thead || !tbody) return;

  thead.replaceChildren();
  tbody.replaceChildren();

  const headerRow = document.createElement("tr");
  (data.columns || []).forEach((name) => {
    const th = document.createElement("th");
    th.textContent = name;
    headerRow.appendChild(th);
  });
  thead.appendChild(headerRow);

  (data.rows || []).forEach((row) => {
    const tr = document.createElement("tr");
    (data.columns || []).forEach((name) => {
      const td = document.createElement("td");
      const value = row[name];
      td.textContent = value == null || value === "" ? "—" : String(value);
      tr.appendChild(td);
    });
    tbody.appendChild(tr);
  });

  if (adminSqlHint) {
    adminSqlHint.textContent = data.truncated
      ? `Showing first ${data.rowCount} rows (truncated).`
      : `${data.rowCount ?? 0} row(s).`;
  }
};

const runAdminSql = async () => {
  if (!analyticsEndpoint) {
    if (adminSqlError) {
      adminSqlError.hidden = false;
      adminSqlError.textContent = "Analytics API endpoint is not configured.";
    }
    return;
  }

  const key = requireAdminKey();
  if (!key) return;

  if (adminSqlError) adminSqlError.hidden = true;

  const response = await fetch(`${analyticsEndpoint}/api/analytics/query`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Api-Key": key,
    },
    body: JSON.stringify({ sql: adminSqlInput?.value || "" }),
    mode: "cors",
  });

  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    if (adminSqlError) {
      adminSqlError.hidden = false;
      adminSqlError.textContent =
        response.status === 401
          ? "Invalid API key."
          : payload.error || `Query failed (${response.status}).`;
    }
    return;
  }

  localStorage.setItem(ADMIN_KEY_STORAGE, key);
  if (adminClear) adminClear.hidden = false;
  if (adminStats) adminStats.hidden = false;
  renderSqlResult(payload);
};

const loadAdminSchema = async () => {
  if (!analyticsEndpoint) return;
  const key = requireAdminKey();
  if (!key) return;

  const response = await fetch(`${analyticsEndpoint}/api/analytics/schema`, {
    headers: { "X-Api-Key": key },
    mode: "cors",
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    if (adminSqlError) {
      adminSqlError.hidden = false;
      adminSqlError.textContent =
        response.status === 401
          ? "Invalid API key."
          : payload.error || `Could not load schema (${response.status}).`;
    }
    return;
  }

  if (!adminSchema) return;
  adminSchema.replaceChildren();
  (payload.tables || []).forEach((table) => {
    const p = document.createElement("p");
    const cols = (table.columns || [])
      .map((c) => `${c.name}${c.primaryKey ? " PK" : ""}`)
      .join(", ");
    p.innerHTML = `<strong>${table.name}</strong>: <code>${cols}</code>`;
    adminSchema.appendChild(p);
  });
};

if (adminSqlForm) {
  adminSqlForm.addEventListener("submit", (event) => {
    event.preventDefault();
    runAdminSql().catch(() => {
      if (adminSqlError) {
        adminSqlError.hidden = false;
        adminSqlError.textContent = "Network error talking to the analytics API.";
      }
    });
  });
}

if (adminSqlSchemaBtn) {
  adminSqlSchemaBtn.addEventListener("click", () => {
    loadAdminSchema().catch(() => {});
  });
}

