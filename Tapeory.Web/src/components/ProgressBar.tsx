import "./progressBar.css";

interface ProgressBarProps {
  /** 0…1; null shows a running bar for work whose length isn't known. */
  fraction: number | null;
  /** What is happening right now, shown under the bar and read out by screen readers. */
  status: string;
}

export function ProgressBar({ fraction, status }: ProgressBarProps) {
  const percent = fraction === null ? undefined : Math.round(Math.min(1, Math.max(0, fraction)) * 100);

  return (
    <div className="progress">
      <div
        className={`progress__track${fraction === null ? " progress__track--running" : ""}`}
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent}
        aria-valuetext={status}
      >
        <div className="progress__bar" style={percent === undefined ? undefined : { width: `${percent}%` }} />
      </div>
      <p className="progress__status" aria-live="polite">
        {status}
      </p>
    </div>
  );
}
