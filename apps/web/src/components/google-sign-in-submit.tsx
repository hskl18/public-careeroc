"use client";

import { useFormStatus } from "react-dom";

import { GoogleIcon } from "@/components/google-icon";

export function GoogleSignInSubmit({ demo = false }: { demo?: boolean }) {
  const { pending } = useFormStatus();
  const label = demo ? "Open the demo" : "Continue with Google";
  const pendingLabel = demo ? "Building your demo workspace..." : "Opening Google...";

  return (
    <button
      type="submit"
      disabled={pending}
      aria-busy={pending}
      className={
        demo
          ? "btn btn-primary w-full py-4 text-sm disabled:cursor-wait"
          : "flex w-full items-center justify-center gap-3 rounded-md border border-[var(--border-active)] bg-[var(--surface-strong)] px-6 py-4 text-base font-semibold text-[var(--ink-black)] transition hover:border-[var(--accent)] hover:bg-[var(--surface-elevated)] disabled:cursor-wait disabled:opacity-70"
      }
    >
      {demo ? null : <GoogleIcon />}
      <span>{pending ? pendingLabel : label}</span>
    </button>
  );
}
