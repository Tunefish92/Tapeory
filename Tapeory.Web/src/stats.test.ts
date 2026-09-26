import { describe, expect, it } from "vitest";
import { formatArea, formatFactor, formatLength, pickComparison } from "./stats";

describe("pickComparison", () => {
  it("returns null below the smallest reference", () => {
    expect(pickComparison(50)).toBeNull();
  });

  it("picks the largest reference the length reaches", () => {
    expect(pickComparison(24_000)).toEqual({ key: "cityBus", factor: 2 });
    expect(pickComparison(297)).toEqual({ key: "a4Sheet", factor: 1 });
  });

  it("uses a marathon for very long runs", () => {
    expect(pickComparison(84_390_000)).toEqual({ key: "marathon", factor: 2 });
  });
});

describe("formatLength", () => {
  it("chooses a sensible unit", () => {
    expect(formatLength(5, "en")).toBe("5 mm");
    expect(formatLength(50, "en")).toBe("5 cm");
    expect(formatLength(12_500, "en")).toBe("12.5 m");
    expect(formatLength(2_300_000, "en")).toBe("2.3 km");
  });

  it("uses the locale's decimal separator", () => {
    expect(formatLength(12_500, "de")).toBe("12,5 m");
  });
});

describe("formatArea", () => {
  it("chooses a sensible unit", () => {
    expect(formatArea(50, "en")).toBe("50 mm²");
    expect(formatArea(1250, "en")).toBe("12.5 cm²");
    expect(formatArea(2_000_000, "en")).toBe("2 m²");
  });
});

describe("formatFactor", () => {
  it("keeps one decimal for small factors and rounds large ones", () => {
    expect(formatFactor(2.34, "en")).toBe("2.3");
    expect(formatFactor(42.7, "en")).toBe("43");
  });
});
