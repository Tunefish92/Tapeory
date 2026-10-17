import { useEffect, useState, type InputHTMLAttributes } from "react";

interface CountInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type" | "value" | "onChange" | "min" | "max"> {
  value: number;
  onChange: (value: number) => void;
  min?: number;
  max?: number;
}

/**
 * A whole-number input that can be cleared while typing. The text is kept as typed and only
 * whole numbers reach `onChange`, kept within min…max; leaving the field puts back the last
 * accepted number. Clamping on every keystroke would turn an emptied field straight back into
 * the minimum, so on a phone "1" could never be replaced by "5".
 */
export function CountInput({ value, onChange, min = 1, max, onBlur, ...rest }: CountInputProps) {
  const [draft, setDraft] = useState(String(value));

  const clamp = (count: number) => Math.max(min, max === undefined ? Math.round(count) : Math.min(max, Math.round(count)));

  // Follow outside changes (e.g. a loaded profile), but leave text that already stands for the value.
  useEffect(() => {
    setDraft((text) => (text.trim() !== "" && clamp(Number(text)) === value ? text : String(value)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value]);

  return (
    <input
      {...rest}
      type="number"
      inputMode="numeric"
      min={min}
      max={max}
      value={draft}
      onChange={(event) => {
        const text = event.target.value;
        setDraft(text);
        const count = Number(text);
        if (text.trim() !== "" && Number.isFinite(count)) onChange(clamp(count));
      }}
      onBlur={(event) => {
        setDraft(String(value));
        onBlur?.(event);
      }}
    />
  );
}
