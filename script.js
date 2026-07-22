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
  contact: "contact",
};

const normalizeRoute = (hash) => {
  const raw = (hash || "").replace(/^#\/?/, "").trim().toLowerCase();
  return routeMap[raw] || "home";
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
    meta.setAttribute("content", theme === "dark" ? "#0a0a0a" : "#ffffff");
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
