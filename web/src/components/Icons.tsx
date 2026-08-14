/**
 * Icônes SVG en ligne.
 *
 * Elles sont intégrées au bundle plutôt que chargées depuis une bibliothèque externe :
 * le nombre d'icônes est réduit et cela évite une dépendance supplémentaire.
 * Toutes sont marquées `aria-hidden` : le libellé accessible est porté par le bouton parent.
 */

interface IconProps {
  size?: number;
  className?: string;
}

function svgProps({ size = 20, className }: IconProps) {
  return {
    width: size,
    height: size,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 2,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true,
    focusable: false,
    className,
  };
}

export const PlayIcon = (props: IconProps) => (
  <svg {...svgProps(props)} fill="currentColor" stroke="none">
    <path d="M8 5v14l11-7z" />
  </svg>
);

export const PauseIcon = (props: IconProps) => (
  <svg {...svgProps(props)} fill="currentColor" stroke="none">
    <path d="M6 4h4v16H6zM14 4h4v16h-4z" />
  </svg>
);

export const PrevIcon = (props: IconProps) => (
  <svg {...svgProps(props)} fill="currentColor" stroke="none">
    <path d="M6 5h2v14H6zM20 5v14l-11-7z" />
  </svg>
);

export const NextIcon = (props: IconProps) => (
  <svg {...svgProps(props)} fill="currentColor" stroke="none">
    <path d="M16 5h2v14h-2zM4 5l11 7-11 7z" />
  </svg>
);

export const ShuffleIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M16 3h5v5M4 20 21 3M21 16v5h-5M15 15l6 6M4 4l5 5" />
  </svg>
);

export const RepeatIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="m17 2 4 4-4 4M3 11v-1a4 4 0 0 1 4-4h14M7 22l-4-4 4-4M21 13v1a4 4 0 0 1-4 4H3" />
  </svg>
);

export const RepeatOneIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="m17 2 4 4-4 4M3 11v-1a4 4 0 0 1 4-4h14M7 22l-4-4 4-4M21 13v1a4 4 0 0 1-4 4H3" />
    <path d="M11 10h1v4" strokeWidth={2.4} />
  </svg>
);

export const HeartIcon = ({ filled = false, ...props }: IconProps & { filled?: boolean }) => (
  <svg {...svgProps(props)} fill={filled ? 'currentColor' : 'none'}>
    <path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 0 0-7.8 7.8l8.8 8.8 8.8-8.8a5.5 5.5 0 0 0 0-7.8z" />
  </svg>
);

export const VolumeIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M11 5 6 9H2v6h4l5 4z" />
    <path d="M15.5 8.5a5 5 0 0 1 0 7M19 5a9 9 0 0 1 0 14" />
  </svg>
);

export const MuteIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M11 5 6 9H2v6h4l5 4z" />
    <path d="m23 9-6 6M17 9l6 6" />
  </svg>
);

export const QueueIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M3 6h13M3 12h9M3 18h9M17 12v7M17 12l4-2v7" />
  </svg>
);

export const SearchIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <circle cx="11" cy="11" r="7" />
    <path d="m21 21-4.3-4.3" />
  </svg>
);

export const HomeIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="m3 10 9-7 9 7v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
    <path d="M9 22V12h6v10" />
  </svg>
);

export const LibraryIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M4 3v18M9 3v18M14 4l5 17" />
  </svg>
);

export const UploadIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
    <path d="m7 9 5-5 5 5M12 4v12" />
  </svg>
);

export const UserIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <circle cx="12" cy="8" r="4" />
    <path d="M4 21c0-4 3.6-6 8-6s8 2 8 6" />
  </svg>
);

export const ChartIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M3 3v18h18M7 15v3M12 10v8M17 6v12" />
  </svg>
);

export const ShieldIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M12 3l8 3v6c0 5-3.4 8.3-8 9-4.6-.7-8-4-8-9V6z" />
  </svg>
);

export const SettingsIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <circle cx="12" cy="12" r="3" />
    <path d="M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-2.7 1.1V21a2 2 0 1 1-4 0v-.1A1.6 1.6 0 0 0 7 19.4a1.6 1.6 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1A1.6 1.6 0 0 0 3 15H3a2 2 0 1 1 0-4h.1A1.6 1.6 0 0 0 4.6 9a1.6 1.6 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1A1.6 1.6 0 0 0 9 4.6h.1A1.6 1.6 0 0 0 10 3.1V3a2 2 0 1 1 4 0v.1A1.6 1.6 0 0 0 15 4.6a1.6 1.6 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8v.1a1.6 1.6 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1z" />
  </svg>
);

export const HistoryIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M3 12a9 9 0 1 0 3-6.7L3 8" />
    <path d="M3 3v5h5M12 7v5l3 2" />
  </svg>
);

export const PlusIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M12 5v14M5 12h14" />
  </svg>
);

export const TrashIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14" />
  </svg>
);

export const CloseIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M18 6 6 18M6 6l12 12" />
  </svg>
);

export const ChevronDownIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="m6 9 6 6 6-6" />
  </svg>
);

export const FlagIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M4 21V4h11l-1 3h6l-2 5 2 5h-9l-1-3H4" />
  </svg>
);

export const SunIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
  </svg>
);

export const MoonIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
  </svg>
);

export const MoreIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <circle cx="12" cy="5" r="1.5" fill="currentColor" />
    <circle cx="12" cy="12" r="1.5" fill="currentColor" />
    <circle cx="12" cy="19" r="1.5" fill="currentColor" />
  </svg>
);

export const MusicIcon = (props: IconProps) => (
  <svg {...svgProps(props)}>
    <path d="M9 18V5l12-2v13" />
    <circle cx="6" cy="18" r="3" />
    <circle cx="18" cy="16" r="3" />
  </svg>
);
