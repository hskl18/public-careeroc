"use client";

import { useEffect, useRef } from "react";
import type { KeyboardEvent, MouseEvent, ReactNode } from "react";

type UserMenuProps = {
  children: ReactNode;
  summary: ReactNode;
};

export function UserMenu({ children, summary }: UserMenuProps) {
  const detailsRef = useRef<HTMLDetailsElement>(null);

  function closeMenu() {
    if (detailsRef.current) {
      detailsRef.current.open = false;
    }
  }

  useEffect(() => {
    function handlePointerDown(event: PointerEvent) {
      const menu = detailsRef.current;
      if (!menu?.open || !(event.target instanceof Node)) {
        return;
      }

      if (!menu.contains(event.target)) {
        closeMenu();
      }
    }

    function handleFocusIn(event: FocusEvent) {
      const menu = detailsRef.current;
      if (!menu?.open || !(event.target instanceof Node)) {
        return;
      }

      if (!menu.contains(event.target)) {
        closeMenu();
      }
    }

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("focusin", handleFocusIn);

    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("focusin", handleFocusIn);
    };
  }, []);

  function handleClickCapture(event: MouseEvent<HTMLDetailsElement>) {
    if (
      event.target instanceof Element &&
      event.target.closest("a[href], button")
    ) {
      closeMenu();
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDetailsElement>) {
    if (event.key === "Escape") {
      closeMenu();
    }
  }

  return (
    <details
      ref={detailsRef}
      className="user-menu relative"
      onClickCapture={handleClickCapture}
      onKeyDown={handleKeyDown}
    >
      <summary
        aria-label="Open user menu"
        className="user-menu-summary flex items-center gap-2.5 rounded-md border border-transparent px-2 py-1 transition-colors hover:border-[var(--border)] hover:bg-[var(--surface)]"
      >
        {summary}
      </summary>

      {children}
    </details>
  );
}
