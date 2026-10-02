import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";

interface PrintModeSwitchProps {
  templateId: number;
  mode: "single" | "bulk";
  /** Bulk printing fills fields from data, so a template without fields has only the single print. */
  bulkAvailable: boolean;
}

/** The two ways to print a template, as a large switch at the top of both print pages. */
export function PrintModeSwitch({ templateId, mode, bulkAvailable }: PrintModeSwitchProps) {
  const { t } = useTranslation();
  const options = [
    { key: "single", to: `/templates/${templateId}/print`, available: true },
    { key: "bulk", to: `/templates/${templateId}/bulk-print`, available: bulkAvailable },
  ] as const;

  return (
    <nav className="print-mode" aria-label={t("printing.mode.label")}>
      {options.map((option) => {
        const content = (
          <>
            <strong>{t(`printing.mode.${option.key}`)}</strong>
            <span>{t(`printing.mode.${option.key}Hint`)}</span>
          </>
        );

        return option.available ? (
          <Link
            key={option.key}
            to={option.to}
            className={`print-mode__option${mode === option.key ? " print-mode__option--selected" : ""}`}
            aria-current={mode === option.key ? "page" : undefined}
          >
            {content}
          </Link>
        ) : (
          <span key={option.key} className="print-mode__option print-mode__option--unavailable" title={t("printing.mode.bulkNeedsFields")}>
            {content}
          </span>
        );
      })}
    </nav>
  );
}
