import { useEffect, useState, type InputHTMLAttributes } from "react";

interface NumberInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type" | "value" | "onChange" | "min" | "max"> {
  value: number;
  onChange: (value: number) => void;
  min?: number;
  max?: number;
  /** Whole numbers only (counts, ports); typed decimals are rounded. */
  integer?: boolean;
}

/**
 * A number input that can be cleared while typing. The text is kept as typed and only numbers
 * reach `onChange`, kept within min…max; leaving the field puts back the last accepted number.
 * Converting on every keystroke would turn an emptied field straight into 0 or the minimum, so
 * on a phone "1" could never be replaced by "5".
 */
export function NumberInput({ value, onChange, min, max, integer = false, onBlur, ...rest }: NumberInputProps) {
  const [draft, setDraft] = useState(String(value));

  const accept = (number: number) => {
    const whole = integer ? Math.round(number) : number;
    return Math.min(max ?? Infinity, Math.max(min ?? -Infinity, whole));
  };

  // Follow outside changes (e.g. a dragged object or a loaded profile), but leave text that
  // already stands for the value, such as "1." or "05" while typing.
  useEffect(() => {
    setDraft((text) => (text.trim() !== "" && accept(Number(text)) === value ? text : String(value)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value]);

  return (
    <input
      {...rest}
      type="number"
      inputMode={integer ? "numeric" : "decimal"}
      min={min}
      max={max}
      value={draft}
      onChange={(event) => {
        const text = event.target.value;
        setDraft(text);
        const number = Number(text);
        if (text.trim() !== "" && Number.isFinite(number)) onChange(accept(number));
      }}
      onBlur={(event) => {
        setDraft(String(value));
        onBlur?.(event);
      }}
    />
  );
}
