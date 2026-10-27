// Setup for the `node` project (vitest.config.ts): logic tests that never touch the DOM.
// Pin the locale so USER_LOCALE (captured once at module load in lib/formatters) is
// deterministic across CI runners. setup.ts does the same for the dom project.
Object.defineProperty(globalThis.navigator, 'language', { value: 'en-US', configurable: true });
