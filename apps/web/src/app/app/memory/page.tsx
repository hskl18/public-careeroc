import { requireUser } from "@/lib/require-user";
import dynamic from "next/dynamic";
import { getMemoryReviewForUser } from "@/lib/profile-server";

const ProfileMemoryPanel = dynamic(() =>
  import("@/components/profile-memory-panel").then(
    (module) => module.ProfileMemoryPanel,
  ),
);

export default async function MemoryPage() {
  const user = await requireUser();
  const { profile, pendingMemory, source: profileSource, proxyPrefix } =
    await getMemoryReviewForUser(user);

  return (
    <main className="app-scroll-main">
      <div className="workspace-shell flex w-full flex-col gap-3 px-3 py-4 animate-in sm:gap-4 sm:px-5 sm:py-6">
        <h1 className="shrink-0 text-xl font-semibold text-[var(--text-primary)]">Memory</h1>

        {profileSource === "fallback" ? (
          <div role="status" className="alert alert-warning">
            API unavailable.
          </div>
        ) : null}

        <ProfileMemoryPanel
          proxyPrefix={proxyPrefix}
          initialMemory={profile.memory ?? []}
          initialPendingMemory={pendingMemory}
        />
      </div>
    </main>
  );
}
