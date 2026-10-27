export type PublicSignalIconName =
  | "bot"
  | "clock"
  | "database"
  | "lock"
  | "mail"
  | "memory"
  | "pipeline"
  | "play"
  | "review"
  | "shield"
  | "trace"
  | "trash";

type PublicSignalIconProps = {
  icon: PublicSignalIconName;
  className?: string;
};

export function PublicSignalIcon({
  icon,
  className = "",
}: PublicSignalIconProps) {
  const commonPathProps = {
    stroke: "currentColor",
    strokeWidth: 2,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
  };

  return (
    <span
      className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-[var(--brand-blue)]/14 bg-[var(--blue-soft)] text-[var(--brand-blue)] ${className}`}
    >
      <svg
        aria-hidden="true"
        viewBox="0 0 24 24"
        className="h-[1.125rem] w-[1.125rem]"
        fill="none"
      >
        {icon === "bot" ? (
          <>
            <rect {...commonPathProps} x="5" y="7" width="14" height="11" rx="2" />
            <path {...commonPathProps} d="M9 7V4m6 3V4M9 13h.01M15 13h.01M10 18v2m4-2v2" />
          </>
        ) : null}
        {icon === "clock" ? (
          <>
            <circle {...commonPathProps} cx="12" cy="12" r="8" />
            <path {...commonPathProps} d="M12 8v4l3 2" />
          </>
        ) : null}
        {icon === "database" ? (
          <>
            <ellipse {...commonPathProps} cx="12" cy="5" rx="7" ry="3" />
            <path {...commonPathProps} d="M5 5v6c0 1.7 3.1 3 7 3s7-1.3 7-3V5" />
            <path {...commonPathProps} d="M5 11v6c0 1.7 3.1 3 7 3s7-1.3 7-3v-6" />
          </>
        ) : null}
        {icon === "lock" ? (
          <>
            <rect {...commonPathProps} x="5" y="10" width="14" height="10" rx="2" />
            <path {...commonPathProps} d="M8 10V7a4 4 0 0 1 8 0v3" />
          </>
        ) : null}
        {icon === "mail" ? (
          <>
            <path {...commonPathProps} d="M4 6h16v12H4z" />
            <path {...commonPathProps} d="m4 7 8 6 8-6" />
          </>
        ) : null}
        {icon === "memory" ? (
          <>
            <rect {...commonPathProps} x="6" y="6" width="12" height="12" rx="2" />
            <path {...commonPathProps} d="M9 3v3m3-3v3m3-3v3M9 18v3m3-3v3m3-3v3M3 9h3m-3 3h3m-3 3h3m12-6h3m-3 3h3m-3 3h3" />
          </>
        ) : null}
        {icon === "pipeline" ? (
          <>
            <path {...commonPathProps} d="M4 6h6v6H4zM14 12h6v6h-6z" />
            <path {...commonPathProps} d="M10 9h2a2 2 0 0 1 2 2v1" />
          </>
        ) : null}
        {icon === "play" ? (
          <path {...commonPathProps} d="M8 5v14l11-7z" />
        ) : null}
        {icon === "review" ? (
          <>
            <path {...commonPathProps} d="M5 4h14v16H5z" />
            <path {...commonPathProps} d="m8 12 2 2 5-5M8 17h8" />
          </>
        ) : null}
        {icon === "shield" ? (
          <>
            <path {...commonPathProps} d="M12 3 5 6v5c0 4.5 3 8 7 10 4-2 7-5.5 7-10V6z" />
            <path {...commonPathProps} d="m9 12 2 2 4-4" />
          </>
        ) : null}
        {icon === "trace" ? (
          <>
            <circle {...commonPathProps} cx="6" cy="7" r="2" />
            <circle {...commonPathProps} cx="18" cy="17" r="2" />
            <path {...commonPathProps} d="M8 7h4a4 4 0 0 1 4 4v4" />
          </>
        ) : null}
        {icon === "trash" ? (
          <>
            <path {...commonPathProps} d="M4 7h16M10 11v6m4-6v6M6 7l1 13h10l1-13M9 7V4h6v3" />
          </>
        ) : null}
      </svg>
    </span>
  );
}
