import { useState, type PropsWithChildren } from "react";
import { NavLink } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { AccountMenu, OpenAccessBanner } from "./AccountMenu";
import { Footer } from "./Footer";
import "./AppLayout.css";

const NAV_ITEMS = [
  { key: "dashboard", to: "/", adminOnly: false },
  { key: "templates", to: "/templates", adminOnly: false },
  { key: "printJobs", to: "/print-jobs", adminOnly: false },
  { key: "printers", to: "/printers", adminOnly: true },
  { key: "settings", to: "/settings", adminOnly: false },
  { key: "about", to: "/about", adminOnly: false },
] as const;

export function AppLayout({ children }: PropsWithChildren) {
  const { t } = useTranslation();
  const [navOpen, setNavOpen] = useState(false);
  const { canAdminister } = useAuth();
  const navItems = NAV_ITEMS.filter((item) => canAdminister || !item.adminOnly);

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        {t("common.back")}
      </a>
      <header className="app-header">
        <div className="app-header__inner">
          <div className="app-header__brand">
            <img src="/logo.svg" alt="" width={28} height={28} className="app-header__logo" />
            <h1>{t("app.name")}</h1>
          </div>
          <button
            type="button"
            className="app-header__menu-toggle"
            data-testid="mobile-nav-toggle"
            aria-label={t("common.menu")}
            aria-controls="primary-nav"
            aria-expanded={navOpen}
            onClick={() => setNavOpen((open) => !open)}
          >
            <span className="app-header__menu-icon" aria-hidden="true" />
          </button>
          <nav aria-label="Primary" id="primary-nav" className={navOpen ? "is-open" : undefined}>
            <ul>
              {navItems.map((item) => (
                <li key={item.key}>
                  <NavLink to={item.to} end={item.to === "/"} onClick={() => setNavOpen(false)}>
                    {t(`nav.${item.key}`)}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
          <AccountMenu />
        </div>
      </header>
      <OpenAccessBanner />
      <main className="app-main" id="main-content">
        {children}
      </main>
      <Footer />
    </div>
  );
}
