// Line-icon set ported verbatim from the Claude Design project (icons.js).
// Inline SVG painted with currentColor and sized from the surrounding font-size,
// so an icon inherits colour and scale from its context and needs no icon font.

export type IconName =
  | 'arrow-counter-clockwise'
  | 'arrow-line-up-right'
  | 'arrow-square-in'
  | 'arrow-square-out'
  | 'arrow-u-down-left'
  | 'bell'
  | 'bell-ringing'
  | 'buildings'
  | 'calendar-blank'
  | 'calendar-check'
  | 'calendar-plus'
  | 'car'
  | 'car-profile'
  | 'car-simple'
  | 'caret-down'
  | 'caret-left'
  | 'caret-right'
  | 'chart-line-up'
  | 'chat-circle'
  | 'chat-circle-dots'
  | 'check'
  | 'check-circle'
  | 'check-square'
  | 'clock'
  | 'clock-counter-clockwise'
  | 'cloud-slash'
  | 'credit-card'
  | 'currency-circle-dollar'
  | 'desktop'
  | 'device-mobile'
  | 'device-tablet'
  | 'download-simple'
  | 'eye'
  | 'eye-slash'
  | 'file-lock'
  | 'file-x'
  | 'gauge'
  | 'gavel'
  | 'gear'
  | 'gear-six'
  | 'identification-card'
  | 'image'
  | 'info'
  | 'key'
  | 'laptop'
  | 'list'
  | 'list-magnifying-glass'
  | 'lock-key'
  | 'lock-key-open'
  | 'lock-simple'
  | 'magnifying-glass'
  | 'map-pin'
  | 'moped'
  | 'mountains'
  | 'paperclip'
  | 'pause-circle'
  | 'pencil-simple'
  | 'plus'
  | 'plus-circle'
  | 'prohibit'
  | 'question'
  | 'receipt'
  | 'scales'
  | 'shield-check'
  | 'sign-out'
  | 'sliders-horizontal'
  | 'squares-four'
  | 'star'
  | 'star-fill'
  | 'steering-wheel'
  | 'storefront'
  | 'toggle-left'
  | 'toggle-right'
  | 'tray'
  | 'user'
  | 'user-circle-plus'
  | 'user-gear'
  | 'user-minus'
  | 'user-plus'
  | 'users-three'
  | 'warning'
  | 'warning-circle'
  | 'warning-diamond'
  | 'x'
  | 'x-circle';

export const ICON_PATHS: Readonly<Record<IconName, string>> = Object.freeze({
  'arrow-counter-clockwise': '<path d="M4 9a8.5 8.5 0 1 1-1 6"/><path d="M4 4v5h5"/>',
  'arrow-line-up-right': '<path d="M7 17 17 7M9.5 7H17v7.5M4 21h16"/>',
  'arrow-u-down-left': '<path d="M18 5v8a4.5 4.5 0 0 1-9 0V5"/><path d="M12.5 8.5 9 5 5.5 8.5"/>',
  bell: '<path d="M6.5 10a5.5 5.5 0 0 1 11 0c0 4 1.5 5.5 1.5 5.5H5S6.5 14 6.5 10ZM10 18.5a2.2 2.2 0 0 0 4 0"/>',
  buildings: '<path d="M3.5 20.5V8.5h8v12M11.5 20.5v-9h9v9M6 12h3M6 16h3M14.5 15h3"/>',
  'calendar-blank':
    '<rect x="3.5" y="5" width="17" height="15.5" rx="2"/><path d="M8 3v4M16 3v4M3.5 10h17"/>',
  'calendar-check':
    '<rect x="3.5" y="5" width="17" height="15.5" rx="2"/><path d="M8 3v4M16 3v4M3.5 10h17M9 15l2.2 2.2L15.5 13"/>',
  car: '<path d="M3.5 16h17M5 16l1.8-6.2A2 2 0 0 1 8.7 8.3h6.6a2 2 0 0 1 1.9 1.5L19 16M3.5 16v3H7v-3M17 16v3h3.5v-3"/><circle cx="8" cy="13" r=".9"/><circle cx="16" cy="13" r=".9"/>',
  'car-simple':
    '<path d="M4 15.5h16M5.5 15.5 7.5 9h9l2 6.5M4 15.5v3h2.5v-3M17.5 15.5v3H20v-3"/><path d="M8 12.5h8"/>',
  'caret-down': '<path d="m6 9.5 6 6 6-6"/>',
  'caret-left': '<path d="m14.5 6-6 6 6 6"/>',
  'caret-right': '<path d="m9.5 6 6 6-6 6"/>',
  'chart-line-up': '<path d="M3.5 20V4M3.5 20h17M6.5 16l4-4.5 3 2.5 5-6"/><path d="M15.5 8h3v3"/>',
  'chat-circle-dots':
    '<path d="M12 20.5a8.5 8.5 0 1 0-7.6-4.7L3.5 20.5l4.7-.9c1.1.6 2.4.9 3.8.9Z"/><path d="M8.5 12h.1M12 12h.1M15.5 12h.1"/>',
  'check-circle': '<circle cx="12" cy="12" r="9"/><path d="m8 12.2 2.6 2.6L16.2 9"/>',
  'cloud-slash':
    '<path d="M7 18.5h9.5a4 4 0 0 0 .6-8 5.5 5.5 0 0 0-9.7-1.6A4.5 4.5 0 0 0 7 18.5Z"/><path d="M4 20.5 20 3.5"/>',
  'credit-card':
    '<rect x="2.5" y="5.5" width="19" height="13" rx="2"/><path d="M2.5 10h19M6 15h4"/>',
  desktop: '<rect x="3" y="4.5" width="18" height="11.5" rx="1.6"/><path d="M9 19.5h6M12 16v3.5"/>',
  'device-mobile': '<rect x="7" y="2.5" width="10" height="19" rx="2"/><path d="M10.5 18.5h3"/>',
  'device-tablet': '<rect x="5" y="2.5" width="14" height="19" rx="2"/><path d="M10.5 18.5h3"/>',
  'download-simple': '<path d="M12 4v11M7.5 11 12 15.5 16.5 11M4.5 19.5h15"/>',
  'eye-slash':
    '<path d="M2.5 12S6 6.5 12 6.5s9.5 5.5 9.5 5.5-3.5 5.5-9.5 5.5S2.5 12 2.5 12Z"/><path d="M4 20 20 4"/>',
  'file-lock':
    '<path d="M6 3.5h7l5 5v12H6z"/><path d="M13 3.5v5h5"/><rect x="9" y="12" width="6" height="5" rx="1"/><path d="M10.5 12v-1.3a1.5 1.5 0 0 1 3 0V12"/>',
  gavel:
    '<path d="M4 20h9M9.5 4.5 15 10M6.5 7.5 12 13"/><rect x="11.5" y="3" width="9" height="4.5" rx="1.4" transform="rotate(45 16 5.2)"/>',
  'gear-six':
    '<circle cx="12" cy="12" r="3.2"/><path d="M12 3v2.4M12 18.6V21M4.2 7.5l2.1 1.2M17.7 15.3l2.1 1.2M4.2 16.5l2.1-1.2M17.7 8.7l2.1-1.2"/>',
  'identification-card':
    '<rect x="2.5" y="5" width="19" height="14" rx="2"/><circle cx="8.5" cy="11" r="2"/><path d="M5.5 16c.6-1.6 1.7-2.4 3-2.4s2.4.8 3 2.4M14.5 10h4M14.5 14h4"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5.5M12 7.7v.1"/>',
  laptop: '<rect x="4.5" y="5.5" width="15" height="10" rx="1.6"/><path d="M2.5 18.5h19"/>',
  list: '<path d="M4 6h16M4 12h16M4 18h11"/>',
  'list-magnifying-glass':
    '<path d="M3.5 6h12M3.5 11h8M3.5 16h6"/><circle cx="16.5" cy="15.5" r="3.5"/><path d="m19.2 18.2 2.3 2.3"/>',
  'lock-key':
    '<rect x="4.5" y="10" width="15" height="10.5" rx="2"/><path d="M8 10V7.5a4 4 0 0 1 8 0V10"/><circle cx="12" cy="14.5" r="1.4"/><path d="M12 16v2"/>',
  'magnifying-glass': '<circle cx="11" cy="11" r="6.5"/><path d="m15.8 15.8 4.2 4.2"/>',
  'map-pin':
    '<path d="M12 21s6.5-6 6.5-11a6.5 6.5 0 0 0-13 0C5.5 15 12 21 12 21Z"/><circle cx="12" cy="10" r="2.4"/>',
  paperclip:
    '<path d="M17 8.5 9.5 16a3 3 0 0 0 4.2 4.2L21 13"/><path d="M20.5 9.5 13 17"/><path d="M17 8.5 12.5 13"/><path d="M17 8.5A3.2 3.2 0 1 0 12.5 4L5 11.5a5.5 5.5 0 0 0 7.8 7.8"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  prohibit: '<circle cx="12" cy="12" r="9"/><path d="M5.6 18.4 18.4 5.6"/>',
  question:
    '<circle cx="12" cy="12" r="9"/><path d="M9.6 9.4a2.5 2.5 0 1 1 3.6 2.3c-.8.4-1.2 1-1.2 1.9M12 16.6v.1"/>',
  receipt: '<path d="M6 3.5h12v17l-3-1.6-3 1.6-3-1.6-3 1.6z"/><path d="M9 8h6M9 12h6"/>',
  scales:
    '<path d="M12 4v16M7 20h10M4 8h16M4 8 1.5 14h5zM20 8l2.5 6h-5z"/><circle cx="12" cy="4.5" r="1.3"/>',
  'shield-check':
    '<path d="M12 3l7.5 3v5.5c0 4.5-3.2 7.6-7.5 9.5-4.3-1.9-7.5-5-7.5-9.5V6z"/><path d="m8.8 12 2.3 2.3 4.1-4.6"/>',
  'sign-out':
    '<path d="M15 4.5H19a1.5 1.5 0 0 1 1.5 1.5v12A1.5 1.5 0 0 1 19 19.5h-4M11 8 7 12l4 4M7 12h9"/>',
  'sliders-horizontal':
    '<path d="M3.5 7h11M18.5 7h2M3.5 17h4M11.5 17h9"/><circle cx="16" cy="7" r="2.2"/><circle cx="9" cy="17" r="2.2"/>',
  'squares-four':
    '<rect x="3.5" y="3.5" width="7" height="7" rx="1.2"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.2"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.2"/><rect x="13.5" y="13.5" width="7" height="7" rx="1.2"/>',
  star: '<path d="M12 3.8l2.6 5.4 5.9.8-4.3 4.1 1 5.9L12 17.3 6.8 20l1-5.9L3.5 10l5.9-.8z"/>',
  'steering-wheel':
    '<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="3"/><path d="M12 9V3M9.5 14 4.5 18M14.5 14l5 4"/>',
  storefront: '<path d="M3.5 9.5V20h17V9.5M2.5 9.5 5 4h14l2.5 5.5zM9.5 20v-6h5v6"/>',
  'toggle-left':
    '<rect x="2.5" y="7" width="19" height="10" rx="5"/><circle cx="7.5" cy="12" r="2.8"/>',
  tray: '<rect x="3.5" y="4.5" width="17" height="15" rx="2"/><path d="M3.5 14h4l1.5 2.5h6L16.5 14h4"/>',
  user: '<circle cx="12" cy="8.5" r="3.5"/><path d="M5.5 20c1-3.4 3.4-5 6.5-5s5.5 1.6 6.5 5"/>',
  'user-gear':
    '<circle cx="9.5" cy="8.5" r="3.5"/><path d="M3.5 20c.9-3.4 3-5 6-5"/><circle cx="17.5" cy="17" r="2.4"/><path d="M17.5 13v1.4M17.5 19.6V21M13.9 15l1.2.7M19.9 18.3l1.2.7M13.9 21l1.2-.7M19.9 15.7l1.2-.7"/>',
  'user-minus':
    '<circle cx="10" cy="8.5" r="3.5"/><path d="M3.5 20c1-3.4 3.4-5 6.5-5M15.5 16.5h5.5"/>',
  'users-three':
    '<circle cx="12" cy="9" r="3"/><circle cx="4.8" cy="10.5" r="2.3"/><circle cx="19.2" cy="10.5" r="2.3"/><path d="M7 19c.9-2.6 2.7-4 5-4s4.1 1.4 5 4M1.5 17.5c.5-1.7 1.6-2.7 3.3-2.7M19.2 14.8c1.7 0 2.8 1 3.3 2.7"/>',
  warning: '<path d="M12 3.5 21.5 20H2.5z"/><path d="M12 9.5v4.5M12 17.2v.1"/>',
  'warning-circle': '<circle cx="12" cy="12" r="9"/><path d="M12 7.5v5M12 16.2v.1"/>',
  'warning-diamond': '<path d="M12 2.8 21.2 12 12 21.2 2.8 12z"/><path d="M12 8v5M12 16.2v.1"/>',
  x: '<path d="M6 6l12 12M18 6 6 18"/>',
  'x-circle': '<circle cx="12" cy="12" r="9"/><path d="m9 9 6 6M15 9l-6 6"/>',
  'arrow-square-in':
    '<path d="M20 5.5v13a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 18.5v-13A1.5 1.5 0 0 1 5.5 4h13A1.5 1.5 0 0 1 20 5.5Z"/><path d="M15.5 8.5H9V15M9 8.5 15.5 15" transform="rotate(180 12 11.75)"/>',
  'arrow-square-out':
    '<path d="M13 4.5h6.5V11M19.5 4.5 12 12"/><path d="M17 14.5v4a1.5 1.5 0 0 1-1.5 1.5H5.5A1.5 1.5 0 0 1 4 18.5V8.5A1.5 1.5 0 0 1 5.5 7h4"/>',
  'bell-ringing':
    '<path d="M6.5 10a5.5 5.5 0 0 1 11 0c0 4 1.5 5.5 1.5 5.5H5S6.5 14 6.5 10ZM10 18.5a2.2 2.2 0 0 0 4 0"/><path d="M2.6 8.2A6.6 6.6 0 0 1 5 4M21.4 8.2A6.6 6.6 0 0 0 19 4"/>',
  'calendar-plus':
    '<rect x="3.5" y="5" width="17" height="15.5" rx="2"/><path d="M8 3v4M16 3v4M3.5 10h17M12 13v5M9.5 15.5h5"/>',
  'car-profile':
    '<path d="M3 15.5h18M4.5 15.5V12l2-4.5h8l3.5 4.5h2a1.5 1.5 0 0 1 1.5 1.5v2M4.5 15.5v2.5H7v-2.5M16 15.5V18h2.5v-2.5"/><path d="M13 7.5v4.5H6"/>',
  'chat-circle': '<path d="M12 20.5a8.5 8.5 0 1 0-7.6-4.7L3.5 20.5l4.7-.9c1.1.6 2.4.9 3.8.9Z"/>',
  'check-square':
    '<rect x="3.5" y="3.5" width="17" height="17" rx="2.4"/><path d="m8 12.2 2.6 2.6L16.2 9"/>',
  'clock-counter-clockwise':
    '<path d="M4 9a8.5 8.5 0 1 1-1 6"/><path d="M4 4v5h5"/><path d="M12 8v4.4l3.2 2"/>',
  'currency-circle-dollar':
    '<circle cx="12" cy="12" r="9"/><path d="M9.5 14.6c.4.9 1.3 1.4 2.5 1.4 1.4 0 2.4-.7 2.4-1.9 0-2.6-4.8-1.4-4.8-4 0-1.2 1-1.9 2.4-1.9 1.2 0 2.1.5 2.5 1.4M12 5.8v1.4M12 16v1.4"/>',
  'file-x':
    '<path d="M6 3.5h7l5 5v12H6z"/><path d="M13 3.5v5h5"/><path d="m10 13 4 4M14 13l-4 4"/>',
  'lock-key-open':
    '<rect x="4.5" y="10" width="15" height="10.5" rx="2"/><path d="M8 10V7.5a4 4 0 0 1 7.8-1.3"/><circle cx="12" cy="14.5" r="1.4"/><path d="M12 16v2"/>',
  'lock-simple':
    '<rect x="4.5" y="10" width="15" height="10.5" rx="2"/><path d="M8 10V7.5a4 4 0 0 1 8 0V10"/>',
  'pause-circle': '<circle cx="12" cy="12" r="9"/><path d="M10 9v6M14 9v6"/>',
  'pencil-simple': '<path d="M4 20h4L20 8l-4-4L4 16z"/><path d="m14.5 5.5 4 4"/>',
  'plus-circle': '<circle cx="12" cy="12" r="9"/><path d="M12 8v8M8 12h8"/>',
  'star-fill':
    '<path d="M12 3.8l2.6 5.4 5.9.8-4.3 4.1 1 5.9L12 17.3 6.8 20l1-5.9L3.5 10l5.9-.8z" fill="currentColor" stroke="none"/>',
  'toggle-right':
    '<rect x="2.5" y="7" width="19" height="10" rx="5"/><circle cx="16.5" cy="12" r="2.8"/>',
  'user-circle-plus':
    '<circle cx="12" cy="12" r="9"/><circle cx="12" cy="10" r="2.8"/><path d="M7 18c.9-2 2.7-3.2 5-3.2M16.5 17h4M18.5 15v4"/>',
  'user-plus':
    '<circle cx="10" cy="8.5" r="3.5"/><path d="M3.5 20c1-3.4 3.4-5 6.5-5M18.5 14v5M16 16.5h5"/>',
  check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5.4l3.6 2.2"/>',
  eye: '<path d="M2.5 12S6 6.5 12 6.5s9.5 5.5 9.5 5.5-3.5 5.5-9.5 5.5S2.5 12 2.5 12Z"/><circle cx="12" cy="12" r="2.6"/>',
  gauge:
    '<path d="M4 18a8.5 8.5 0 1 1 16 0"/><path d="m12 12.5 4-4"/><circle cx="12" cy="14" r="1.6"/>',
  gear: '<circle cx="12" cy="12" r="3.2"/><path d="M12 3v2.4M12 18.6V21M4.2 7.5l2.1 1.2M17.7 15.3l2.1 1.2M4.2 16.5l2.1-1.2M17.7 8.7l2.1-1.2M3 12h2.4M18.6 12H21"/>',
  image:
    '<rect x="3" y="4.5" width="18" height="15" rx="2"/><circle cx="8.5" cy="10" r="1.8"/><path d="m4 17 5-4.5 4 3.5 3-2.5 4 3.5"/>',
  key: '<circle cx="8" cy="12" r="4"/><path d="M12 12h9M18 12v3M15.5 12v2.5"/>',
  moped:
    '<circle cx="6" cy="17.5" r="2.6"/><circle cx="18" cy="17.5" r="2.6"/><path d="M8.6 17.5h6.8M4 8.5h4l3.5 9M14 5.5h3l2.5 9M11 8.5h5"/>',
  mountains: '<path d="M2.5 19.5h19L14 7l-3.2 5.2L8.8 10z"/><circle cx="17" cy="6.5" r="2"/>',
});

// Drawn when a name is missing, so a typo shows as a circle rather than a gap.
export const ICON_FALLBACK = '<circle cx="12" cy="12" r="7.5"/>';
