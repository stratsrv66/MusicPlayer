import '@testing-library/jest-dom/vitest';

/**
 * Prothèses des API navigateur absentes de jsdom mais utilisées par l'application.
 * Sans elles, le rendu des composants de lecture échouerait dans les tests.
 */

if (!('mediaSession' in navigator)) {
  Object.defineProperty(navigator, 'mediaSession', {
    value: { metadata: null, playbackState: 'none', setActionHandler: () => {} },
    configurable: true,
  });
}

if (typeof globalThis.MediaMetadata === 'undefined') {
  globalThis.MediaMetadata = class {
    constructor(init: Record<string, unknown>) {
      Object.assign(this, init);
    }
  } as never;
}

// jsdom n'implémente ni la lecture ni le chargement des médias.
Object.defineProperty(HTMLMediaElement.prototype, 'play', {
  value: () => Promise.resolve(),
  configurable: true,
});
Object.defineProperty(HTMLMediaElement.prototype, 'pause', { value: () => {}, configurable: true });
Object.defineProperty(HTMLMediaElement.prototype, 'load', { value: () => {}, configurable: true });

if (!globalThis.crypto?.randomUUID) {
  Object.defineProperty(globalThis.crypto, 'randomUUID', {
    value: () => '00000000-0000-4000-8000-000000000000',
    configurable: true,
  });
}
