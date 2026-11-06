import { SvgAsset, mascotAssets } from "@/components/svg-asset";

export default function WorkspaceLoading() {
  return (
    <main className="app-scroll-main">
      <div className="flex min-h-full w-full items-center justify-center px-5 py-10">
        <div className="card flex max-w-sm flex-col items-center p-8 text-center">
          <SvgAsset
            src={mascotAssets.loadingProgress}
            label="Loading workspace"
            className="h-28 w-44"
          />
          <p className="mt-3 text-sm text-[var(--text-secondary)]">Loading...</p>
        </div>
      </div>
    </main>
  );
}
