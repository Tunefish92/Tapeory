import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { CountInput } from "./CountInput";

function Harness({ max }: { max?: number }) {
  const [count, setCount] = useState(1);
  return (
    <>
      <label>
        Count
        <CountInput value={count} max={max} onChange={setCount} />
      </label>
      <output>{count}</output>
    </>
  );
}

describe("CountInput", () => {
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
});
