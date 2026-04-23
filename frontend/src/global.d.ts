/// <reference types="vite/client" />

/**
 * Build-time global injected by Vite `define` in consumer apps (orkyo-saas,
 * orkyo-community). Foundation itself does not configure Vite — it declares
 * the ambient so shared pages can read the constant safely with a runtime
 * `typeof` guard.
 */
declare const __BUILD_TIME__: string | undefined;
