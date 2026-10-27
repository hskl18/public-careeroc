"use client";

import { useState } from "react";
import type { FormEvent } from "react";

export function SignOutButton() {
  const [pending, setPending] = useState(false);

  function clearBrowserState() {
    try {
      window.sessionStorage.clear();
      window.localStorage.clear();
    } catch {
      // Storage can be unavailable in hardened browser modes.
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const form = event.currentTarget;
    clearBrowserState();
    setPending(true);

    try {
      const response = await fetch(form.action, {
        method: "POST",
        cache: "no-store",
        credentials: "same-origin",
        headers: {
          Accept: "application/json",
        },
      });

      if (!response.ok) {
        throw new Error("Sign out request failed.");
      }

      window.location.replace("/");
    } catch {
      form.submit();
    }
  }

  return (
    <form action="/api/auth/logout" method="post" onSubmit={handleSubmit}>
      <button
        type="submit"
        disabled={pending}
        className="mt-1 flex w-full items-center rounded-md px-3 py-2 text-left text-sm font-medium text-[var(--red)] transition-colors hover:bg-[var(--red-soft)]"
      >
        {pending ? "Signing out..." : "Sign out"}
      </button>
    </form>
  );
}
