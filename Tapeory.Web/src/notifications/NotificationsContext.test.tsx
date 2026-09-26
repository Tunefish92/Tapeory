import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { NotificationsProvider, useNotifications } from "./NotificationsContext";

function Trigger({ message, kind }: { message: string; kind?: "success" | "error" | "info" }) {
  const { notify } = useNotifications();
  return (
    <button type="button" onClick={() => notify(message, kind)}>
      Trigger
    </button>
  );
}

describe("NotificationsProvider / useNotifications", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("shows a notification when notify is called", () => {
    render(
      <NotificationsProvider>
        <Trigger message="Printer added." kind="success" />
      </NotificationsProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Trigger" }));

    const notification = screen.getByRole("status");
    expect(notification).toHaveTextContent("Printer added.");
    expect(notification).toHaveClass("notification--success");
  });

  it("defaults to the 'info' kind when none is given", () => {
    render(
      <NotificationsProvider>
        <Trigger message="Heads up." />
      </NotificationsProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Trigger" }));

    expect(screen.getByRole("status")).toHaveClass("notification--info");
  });

  it("shows multiple notifications independently", () => {
    render(
      <NotificationsProvider>
        <Trigger message="First" />
        <Trigger message="Second" />
      </NotificationsProvider>,
    );

    const [first, second] = screen.getAllByRole("button", { name: "Trigger" });
    fireEvent.click(first);
    fireEvent.click(second);

    expect(screen.getByText("First")).toBeInTheDocument();
    expect(screen.getByText("Second")).toBeInTheDocument();
  });

  it("dismisses a notification when its close button is clicked", () => {
    render(
      <NotificationsProvider>
        <Trigger message="Dismiss me" />
      </NotificationsProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Trigger" }));
    expect(screen.getByText("Dismiss me")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Dismiss" }));
    expect(screen.queryByText("Dismiss me")).not.toBeInTheDocument();
  });

  it("auto-dismisses a notification after the timeout", () => {
    render(
      <NotificationsProvider>
        <Trigger message="Auto-dismiss" />
      </NotificationsProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Trigger" }));
    expect(screen.getByText("Auto-dismiss")).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(5000);
    });

    expect(screen.queryByText("Auto-dismiss")).not.toBeInTheDocument();
  });

  it("does nothing when used outside a NotificationsProvider", () => {
    render(<Trigger message="orphaned" />);

    expect(() => fireEvent.click(screen.getByRole("button", { name: "Trigger" }))).not.toThrow();
  });
});
