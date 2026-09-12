/// <reference types="vite/client" />

/**
 * Declared as possibly undefined on purpose. Vite substitutes these at build
 * time, and a variable that was never set is simply absent, so the validation
 * in src/config.ts is doing real work rather than satisfying the type checker.
 */
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string | undefined
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
