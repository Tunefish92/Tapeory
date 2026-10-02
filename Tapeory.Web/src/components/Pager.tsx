import { useTranslation } from "react-i18next";

interface PagerProps {
  /** The page shown, starting at 0. */
  page: number;
  pageSize: number;
  total: number;
  onPage: (page: number) => void;
  /** The text key for "Rows 101–200 of 5000". */
  infoKey: string;
}

/** Previous/next for a long list shown a page at a time; nothing when it fits on one page. */
export function Pager({ page, pageSize, total, onPage, infoKey }: PagerProps) {
  const { t } = useTranslation();
  if (total <= pageSize) return null;

  const pages = Math.ceil(total / pageSize);

  return (
    <div className="pager">
      <button type="button" className="btn btn-sm" disabled={page <= 0} onClick={() => onPage(page - 1)}>
        <span aria-hidden="true">←</span> {t("printing.bulk.check.previous")}
      </button>
      <span className="pager__info">
        {t(infoKey, { from: page * pageSize + 1, to: Math.min(total, (page + 1) * pageSize), total })}
      </span>
      <button type="button" className="btn btn-sm" disabled={page >= pages - 1} onClick={() => onPage(page + 1)}>
        {t("printing.bulk.check.next")} <span aria-hidden="true">→</span>
      </button>
    </div>
  );
}
