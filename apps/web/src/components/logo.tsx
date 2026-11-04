type LogoProps = {
  className?: string;
  title?: string;
};

export function Logo({ className, title = "careeroc" }: LogoProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 512 512"
      role="img"
      aria-label={title}
      className={className}
    >
      <path
        d="M 92 96 L 156 96 A 16 16 0 0 1 172 112 L 172 304 A 16 16 0 0 0 188 320 L 420 320 A 16 16 0 0 1 436 336 L 436 400 A 16 16 0 0 1 420 416 L 156 416 A 80 80 0 0 1 76 336 L 76 112 A 16 16 0 0 1 92 96 Z"
        fill="var(--logo-primary, #0F172A)"
      />
      <path
        d="M 212 96 L 300 96 A 16 16 0 0 1 316 112 L 316 176 A 16 16 0 0 1 300 192 L 212 192 A 16 16 0 0 1 196 176 L 196 112 A 16 16 0 0 1 212 96 Z"
        fill="var(--logo-accent, #345FA3)"
      />
      <circle cx="388" cy="144" r="48" fill="var(--logo-dot, #159A68)" />
    </svg>
  );
}
