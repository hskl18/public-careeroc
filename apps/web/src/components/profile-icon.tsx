import type { CSSProperties } from "react";

export const defaultProfileIcon = "inbox-blue";

export const profileIconOptions = [
  {
    id: "inbox-blue",
    label: "Inbox Blue",
    src: "/profile-icons/profile-inbox-blue.svg",
  },
  {
    id: "inbox-mint",
    label: "Inbox Mint",
    src: "/profile-icons/profile-inbox-mint.svg",
  },
  {
    id: "inbox-rose",
    label: "Inbox Rose",
    src: "/profile-icons/profile-inbox-rose.svg",
  },
  {
    id: "inbox-alert",
    label: "Inbox Alert",
    src: "/profile-icons/profile-inbox-alert.svg",
  },
  {
    id: "inbox-wink",
    label: "Inbox Wink",
    src: "/profile-icons/profile-inbox-wink.svg",
  },
  {
    id: "inbox-soft",
    label: "Inbox Soft",
    src: "/profile-icons/profile-inbox-soft.svg",
  },
  {
    id: "inbox-sun",
    label: "Inbox Sun",
    src: "/profile-icons/profile-inbox-sun.svg",
  },
  {
    id: "inbox-night",
    label: "Inbox Night",
    src: "/profile-icons/profile-inbox-night.svg",
  },
  {
    id: "inbox-leaf",
    label: "Inbox Leaf",
    src: "/profile-icons/profile-inbox-leaf.svg",
  },
  {
    id: "resume-blue",
    label: "Resume Blue",
    src: "/profile-icons/profile-resume-blue.svg",
  },
  {
    id: "resume-card",
    label: "Resume Card",
    src: "/profile-icons/profile-resume-card.svg",
  },
  {
    id: "resume-stack",
    label: "Resume Stack",
    src: "/profile-icons/profile-resume-stack.svg",
  },
  {
    id: "resume-gold",
    label: "Resume Gold",
    src: "/profile-icons/profile-resume-gold.svg",
  },
  {
    id: "agent-flag",
    label: "Agent Flag",
    src: "/profile-icons/profile-agent-flag.svg",
  },
  {
    id: "agent-radar",
    label: "Agent Radar",
    src: "/profile-icons/profile-agent-radar.svg",
  },
  {
    id: "agent-spark",
    label: "Agent Spark",
    src: "/profile-icons/profile-agent-spark.svg",
  },
  {
    id: "agent-crown",
    label: "Agent Crown",
    src: "/profile-icons/profile-agent-crown.svg",
  },
  {
    id: "star-scout",
    label: "Star Scout",
    src: "/profile-icons/profile-star-scout.svg",
  },
] as const;

export type ProfileIconId = (typeof profileIconOptions)[number]["id"];

export function resolveProfileIcon(icon: string | null | undefined) {
  return (
    profileIconOptions.find((option) => option.id === icon) ??
    profileIconOptions[0]
  );
}

export function ProfileIcon({
  icon,
  label,
  className,
  decorative = false,
}: {
  icon: string | null | undefined;
  label?: string;
  className?: string;
  decorative?: boolean;
}) {
  const option = resolveProfileIcon(icon);
  const style = {
    "--profile-icon-url": `url("${option.src}")`,
  } as CSSProperties;
  const accessibilityProps = decorative
    ? ({ "aria-hidden": true } as const)
    : ({ role: "img", "aria-label": label ?? option.label } as const);

  return (
    <span
      {...accessibilityProps}
      className={`profile-icon ${className ?? ""}`}
      style={style}
    />
  );
}
