// openapi-typescript needs the TypeScript 5 compiler API, which TypeScript 7 no longer ships.
export async function resolve(specifier, context, next) {
  if (specifier === "typescript") return next("typescript-5", { ...context, parentURL: import.meta.url })
  return next(specifier, context)
}
