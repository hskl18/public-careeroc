import { SvgAsset, mascotAssets } from "@/components/svg-asset";

export default function WorkspaceLoading() {
  return (
    <main className="app-scroll-main">
      <div className="mx-auto flex min-h-full w-full max-w-[104rem] items-center justify-center px-5 py-10">
        <div className="card flex max-w-sm flex-col items-center p-8 text-center">
          <SvgAsset
            src={mascotAssets.loadingProgress}
            label="Loading workspace"
            className="h-28 w-44"
          />
          <p className="eyebrow mt-4">Loading workspace</p>
          <p className="mt-2 text-sm text-[var(--text-secondary)]">
            Syncing the latest careeroc state.
          </p>
        </div>
      </div>
    </main>
  );
}
