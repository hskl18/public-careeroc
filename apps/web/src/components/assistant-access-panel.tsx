"use client";

import { useCallback, useState } from "react";
import { LocalTime } from "@/components/local-time";

export type AccessToken = {
  id: string;
  name: string;
  tokenPrefix: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  lastUsedAtUtc: string | null;
  revokedAtUtc: string | null;
};

type AssistantAccessPanelProps = {
  initialTokens: AccessToken[];
  proxyPrefix: string | null;
  mcpUrl: string | null;
  agentCardUrl: string | null;
};

const lifetimeOptions = [
  { days: 7, label: "7 days" },
  { days: 30, label: "30 days" },
  { days: 90, label: "90 days" },
];

async function readError(response: Response) {
  try {
    const payload = (await response.json()) as { error?: string };
    if (payload.error) return payload.error;
  } catch {}
  return `Request failed with ${response.status}`;
}

export function AssistantAccessPanel({ initialTokens, proxyPrefix, mcpUrl, agentCardUrl }: AssistantAccessPanelProps) {
  const [tokens, setTokens] = useState<AccessToken[]>(initialTokens);
  const [name, setName] = useState("My assistant");
  const [lifetimeDays, setLifetimeDays] = useState(30);
  const [newToken, setNewToken] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!proxyPrefix) return;
    const response = await fetch(`${proxyPrefix}/api/profile/access-tokens`, { cache: "no-store" });
    if (response.ok) {
      setTokens((await response.json()) as AccessToken[]);
    }
  }, [proxyPrefix]);

  async function createToken(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!proxyPrefix) return;
    setBusy(true);
    setError(null);
    try {
      const response = await fetch(`${proxyPrefix}/api/profile/access-tokens`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ name, expiresInDays: lifetimeDays }),
      });
      if (!response.ok) throw new Error(await readError(response));
      const created = (await response.json()) as { token: string };
      setNewToken(created.token);
      await load();
    } catch (createError) {
      setError(createError instanceof Error ? createError.message : "Couldn't create a token.");
    } finally {
      setBusy(false);
    }
  }

  async function revokeToken(tokenId: string) {
    if (!proxyPrefix || !window.confirm("Revoke this token? Assistants using it lose access immediately.")) return;
    const response = await fetch(`${proxyPrefix}/api/profile/access-tokens/${tokenId}`, { method: "DELETE" });
    if (!response.ok) {
      setError(await readError(response));
      return;
    }
    await load();
  }

  const activeTokens = tokens.filter((token) => !token.revokedAtUtc && new Date(token.expiresAtUtc) > new Date());
  const tokenForSnippet = newToken ?? "<your token>";

  return (
    <section className="card app-workspace-panel p-5">
      <p className="eyebrow">Assistant access</p>
      <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">Connect your own assistant</h2>
      <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
        Give Claude, Codex, or any MCP or A2A client read-only access to this pipeline. Tokens can see your
        applications, timelines, and reminders. They cannot change anything, and you can revoke them at any time.
      </p>

      {proxyPrefix ? (
        <form onSubmit={createToken} className="mt-4 flex flex-wrap items-end gap-2">
          <label className="flex min-w-48 flex-1 flex-col gap-1 text-xs font-medium text-[var(--text-secondary)]">
            Token name
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={80}
              required
              className="input"
            />
          </label>
          <label className="flex flex-col gap-1 text-xs font-medium text-[var(--text-secondary)]">
            Expires after
            <select
              value={lifetimeDays}
              onChange={(event) => setLifetimeDays(Number(event.target.value))}
              className="input"
            >
              {lifetimeOptions.map((option) => (
                <option key={option.days} value={option.days}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <button type="submit" disabled={busy} className="btn btn-primary btn-sm">
            {busy ? "Creating..." : "Create token"}
          </button>
        </form>
      ) : (
        <p className="alert alert-warning mt-4">Tokens are unavailable while the API is offline.</p>
      )}

      {error ? <p className="alert alert-error mt-3">{error}</p> : null}

      {newToken ? (
        <div className="mt-4 rounded-md border border-[var(--border-active)] bg-[var(--surface-elevated)] p-3">
          <p className="text-xs font-semibold text-[var(--text-primary)]">
            Copy this token now. It will not be shown again.
          </p>
          <code className="mt-2 block break-all text-xs text-[var(--text-primary)]">{newToken}</code>
          <button
            type="button"
            onClick={() => void navigator.clipboard.writeText(newToken)}
            className="btn btn-secondary btn-sm mt-2"
          >
            Copy token
          </button>
        </div>
      ) : null}

      {mcpUrl ? (
        <div className="mt-4 space-y-2">
          <p className="text-xs font-semibold text-[var(--text-primary)]">MCP (Claude Code)</p>
          <pre className="overflow-x-auto rounded-md bg-[var(--surface-strong)] p-3 text-[11px] leading-5 text-[var(--text-primary)]">
            {`claude mcp add --transport http careeroc ${mcpUrl} \\\n  --header "Authorization: Bearer ${tokenForSnippet}"`}
          </pre>
          {agentCardUrl ? (
            <>
              <p className="text-xs font-semibold text-[var(--text-primary)]">A2A agent card</p>
              <code className="block break-all text-[11px] text-[var(--text-secondary)]">{agentCardUrl}</code>
            </>
          ) : null}
        </div>
      ) : null}

      {activeTokens.length > 0 ? (
        <ul className="mt-4 divide-y divide-[var(--border)] border-t border-[var(--border)]">
          {activeTokens.map((token) => (
            <li key={token.id} className="flex items-center justify-between gap-3 py-2 text-sm">
              <span className="min-w-0">
                <span className="block truncate font-medium text-[var(--text-primary)]">{token.name}</span>
                <span className="block text-xs text-[var(--text-tertiary)]">
                  {token.tokenPrefix}... · expires <LocalTime value={token.expiresAtUtc} format="date" />
                  {token.lastUsedAtUtc ? (
                    <>
                      {" "}
                      · last used <LocalTime value={token.lastUsedAtUtc} format="date" />
                    </>
                  ) : (
                    " · never used"
                  )}
                </span>
              </span>
              <button type="button" onClick={() => void revokeToken(token.id)} className="btn btn-secondary btn-sm">
                Revoke
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
