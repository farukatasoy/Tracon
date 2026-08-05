import type { ReactNode } from 'react';

/**
 * Hand-drawn 24×24 stroke icons.
 *
 * An icon package would cost more bytes than the whole icon set used here, and
 * tree shaking does not help when the package ships a single barrel file.
 */
interface IconProps {
  className?: string;
}

function Icon({ children, className }: IconProps & { children: ReactNode }): ReactNode {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={className ?? 'size-4'}
    >
      {children}
    </svg>
  );
}

export function PrismMark({ className }: IconProps): ReactNode {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={className ?? 'size-6'}>
      <defs>
        <linearGradient id="ap-spectrum" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stopColor="var(--ap-violet)" />
          <stop offset="25%" stopColor="var(--ap-indigo)" />
          <stop offset="50%" stopColor="var(--ap-cyan)" />
          <stop offset="75%" stopColor="var(--ap-emerald)" />
          <stop offset="100%" stopColor="var(--ap-amber)" />
        </linearGradient>
      </defs>
      <path
        d="M16 4 29 27H3Z"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinejoin="round"
      />
      <path d="M5.5 21.5h21" stroke="url(#ap-spectrum)" strokeWidth="3" strokeLinecap="round" />
    </svg>
  );
}

export const DashboardIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M4 19V10M10 19V5M16 19v-7M4 19h16" />
  </Icon>
);

export const AgentsIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <rect x="4" y="7" width="16" height="12" rx="3" />
    <path d="M12 3v4M9 12h.01M15 12h.01M9.5 16h5" />
  </Icon>
);

export const PlaygroundIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M4 5h16v11H8l-4 4z" />
    <path d="M8 9h8M8 12.5h5" />
  </Icon>
);

export const SessionsIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M4 6h16M4 12h16M4 18h10" />
    <circle cx="19" cy="18" r="2" />
  </Icon>
);

export const RunsIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M4 12h4l2.5-6 3 12L16 12h4" />
  </Icon>
);

export const ToolsIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M14.5 4.5a4.5 4.5 0 0 0-6 5.9L4 15v4.5h4.5l4.6-4.6a4.5 4.5 0 0 0 5.9-6l-2.9 2.9-2.6-.4-.4-2.6z" />
  </Icon>
);

export const ModelsIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M12 3 20 7.5v9L12 21 4 16.5v-9z" />
    <path d="m4 7.5 8 4.5 8-4.5M12 12v9" />
  </Icon>
);

export const McpIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M4 7h16M4 12h16M4 17h9" />
    <circle cx="18" cy="17" r="2.5" />
  </Icon>
);

export const SettingsIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <circle cx="12" cy="12" r="3" />
    <path d="M12 3v2.2M12 18.8V21M4.2 7.5l1.9 1.1M17.9 15.4l1.9 1.1M4.2 16.5l1.9-1.1M17.9 8.6l1.9-1.1" />
  </Icon>
);

export const SendIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M5 12 19.5 5 13 19l-2-6z" />
  </Icon>
);

export const ChevronIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="m9 5 7 7-7 7" />
  </Icon>
);

export const CheckIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="m5 12.5 4.5 4.5L19 7" />
  </Icon>
);

export const CrossIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M6 6l12 12M18 6 6 18" />
  </Icon>
);

export const SpinnerIcon = ({ className }: IconProps): ReactNode => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    aria-hidden="true"
    className={`${className ?? 'size-4'} animate-spin`}
  >
    <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth="2.5" opacity="0.25" />
    <path
      d="M21 12a9 9 0 0 0-9-9"
      stroke="currentColor"
      strokeWidth="2.5"
      strokeLinecap="round"
    />
  </svg>
);

export const SunIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
  </Icon>
);

export const MoonIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z" />
  </Icon>
);

export const TrashIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M4 7h16M9 7V5h6v2M6 7l1 13h10l1-13" />
  </Icon>
);

export const CopyIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <rect x="9" y="9" width="11" height="11" rx="2" />
    <path d="M15 5H6a2 2 0 0 0-2 2v9" />
  </Icon>
);

export const PlusIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M12 5v14M5 12h14" />
  </Icon>
);

export const HistoryIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M3.5 12a8.5 8.5 0 1 0 2.6-6.1M3.5 4.5V10h5.5" />
    <path d="M12 8v4.5l3 1.7" />
  </Icon>
);

export const PaperclipIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M8 12.5V7a4 4 0 0 1 8 0v9a2.5 2.5 0 0 1-5 0V8" />
  </Icon>
);

export const SpeakerIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M4 9.5h3.5L12 5.5v13L7.5 14.5H4z" />
    <path d="M15.5 9a4 4 0 0 1 0 6M18 6.5a7.5 7.5 0 0 1 0 11" />
  </Icon>
);

export const AuditIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <rect x="5" y="4" width="14" height="17" rx="2" />
    <path d="M9 3.5h6v2H9zM8 10h8M8 13.5h8M8 17h5" />
  </Icon>
);

/** Three boxes wired together: the shape of a workflow graph. */
export const WorkflowIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <rect x="3" y="4" width="6" height="5" rx="1.5" />
    <rect x="15" y="4" width="6" height="5" rx="1.5" />
    <rect x="9" y="15" width="6" height="5" rx="1.5" />
    <path d="M6 9v3.5a1.5 1.5 0 0 0 1.5 1.5H9M18 9v3.5a1.5 1.5 0 0 1-1.5 1.5H15" />
  </Icon>
);

/** A clock face: scheduled and batch jobs run on their own timer. */
export const JobsIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <circle cx="12" cy="12" r="9" />
    <path d="M12 7v5l3.5 2" />
  </Icon>
);

export const EvalsIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M9 11.5 11 13.5 15.5 8.5" />
    <path d="M4 4.5h16v15H4z" />
  </Icon>
);

export const ExperimentsIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <path d="M12 4v5" />
    <circle cx="12" cy="4" r="1.4" />
    <path d="M12 9c0 3.5-4.5 3-4.5 7" />
    <path d="M12 9c0 3.5 4.5 3 4.5 7" />
    <circle cx="7.5" cy="18" r="1.4" />
    <circle cx="16.5" cy="18" r="1.4" />
  </Icon>
);

export const MicIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <rect x="9" y="3" width="6" height="11" rx="3" />
    <path d="M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21M9 21h6" />
  </Icon>
);

export const StopIcon = ({ className }: IconProps): ReactNode => (
  <Icon className={className}>
    <rect x="6" y="6" width="12" height="12" rx="2" />
  </Icon>
);
