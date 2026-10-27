import type { CSSProperties } from "react";

type SvgAssetProps = {
  src: string;
  label?: string;
  className?: string;
  decorative?: boolean;
};

export const mascotAssets = {
  loading: "/mascots/loading/loading-inbox-buddy-bounce.svg",
  loadingProgress: "/mascots/loading/loading-progress-bar.svg",
  loadingResumeScan: "/mascots/loading/loading-resume-scan.svg",
  loadingButtonBuddy: "/mascots/loading/loading-button-buddy.svg",
  resumeBuddyCard: "/mascots/pixel-mail-resume-buddy-card.svg",
  resumeBuddyStack: "/mascots/pixel-mail-resume-buddy-stack.svg",
} as const;

export function SvgAsset({
  src,
  label,
  className,
  decorative = false,
}: SvgAssetProps) {
  const accessibleLabel = label?.trim() || "careeroc asset";
  const accessibilityProps = decorative
    ? ({ "aria-hidden": true } as const)
    : ({ role: "img", "aria-label": accessibleLabel } as const);
  const style = {
    "--svg-asset-url": `url("${src}")`,
  } as CSSProperties;

  return (
    <span
      {...accessibilityProps}
      className={`svg-asset ${className ?? ""}`}
      style={style}
    />
  );
}
