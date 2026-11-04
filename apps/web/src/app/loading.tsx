import { SvgAsset, mascotAssets } from "@/components/svg-asset";

export default function Loading() {
  return (
    <main className="flex min-h-dvh items-center justify-center px-6 py-10">
      <div className="card flex max-w-sm flex-col items-center p-8 text-center">
        <SvgAsset
          src={mascotAssets.loading}
          label="Loading careeroc"
          className="h-32 w-32"
        />
        <p className="eyebrow mt-4">Loading</p>
        <p className="mt-2 text-sm text-[var(--text-secondary)]">
          Preparing your workspace.
        </p>
      </div>
    </main>
  );
}
