import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { NumberInput } from "./NumberInput";

function Harness({ max }: { max?: number }) {
  const [count, setCount] = useState(1);
  return (
    <>
      <label>
        Count
        <NumberInput integer min={1} value={count} max={max} onChange={setCount} />
      </label>
      <output>{count}</output>
    </>
  );
}

describe("NumberInput", () => {
  it("can be emptied and retyped with a single digit", () => {
    render(<Harness />);
    const input = screen.getByLabelText("Count") as HTMLInputElement;

    fireEvent.change(input, { target: { value: "" } });
    expect(input.value).toBe("");

    fireEvent.change(input, { target: { value: "5" } });
    expect(input.value).toBe("5");
    expect(screen.getByRole("status")).toHaveTextContent("5");
  });

  it("puts back the last accepted number when left empty or out of range", () => {
    render(<Harness max={99} />);
    const input = screen.getByLabelText("Count") as HTMLInputElement;

    fireEvent.change(input, { target: { value: "" } });
    fireEvent.blur(input);
    expect(input.value).toBe("1");

    fireEvent.change(input, { target: { value: "150" } });
    expect(screen.getByRole("status")).toHaveTextContent("99");
    fireEvent.blur(input);
    expect(input.value).toBe("99");
  });

  it("takes decimals and negative numbers when not limited to whole numbers", () => {
    function Decimal() {
      const [mm, setMm] = useState(10);
      return (
        <>
          <label>
            X
            <NumberInput value={mm} onChange={setMm} />
          </label>
          <output>{mm}</output>
        </>
      );
    }
    render(<Decimal />);
    const input = screen.getByLabelText("X") as HTMLInputElement;

    fireEvent.change(input, { target: { value: "" } });
    expect(screen.getByRole("status")).toHaveTextContent("10");
    fireEvent.change(input, { target: { value: "-2.5" } });
    expect(screen.getByRole("status")).toHaveTextContent("-2.5");
    expect(input.value).toBe("-2.5");
  });
});
