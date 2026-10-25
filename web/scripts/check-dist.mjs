// Fails when a built app would violate the Content-Security-Policy the backend sends (no inline scripts or styles in index.html,
// no eval-style code generation in the bundles). Run after `pnpm build`.
import { readdirSync, readFileSync, statSync } from "node:fs"
import { join } from "node:path"

const dist = new URL("../dist", import.meta.url).pathname
const problems = []

function* walk(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name)
    if (statSync(path).isDirectory()) yield* walk(path)
    else yield path
  }
}

for (const app of readdirSync(dist)) {
  const html = readFileSync(join(dist, app, "index.html"), "utf8")
  for (const m of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
    if (!/\bsrc=/.test(m[1]) || m[2].trim()) problems.push(`${app}/index.html: inline <script>`)
  }
  if (/<style\b/i.test(html)) problems.push(`${app}/index.html: inline <style>`)
  if (/\sstyle\s*=/i.test(html)) problems.push(`${app}/index.html: style attribute`)
  if (/\son[a-z]+\s*=/i.test(html)) problems.push(`${app}/index.html: inline event handler`)
}

// zod probes `Function("")` inside try/catch and falls back when it throws, so that one is expected.
const allowed = [/try\{return Function\(``\),!0\}catch\{return!1\}/]
for (const file of walk(dist)) {
  if (!file.endsWith(".js")) continue
  const code = readFileSync(file, "utf8")
  for (const m of code.matchAll(/(?<![\w.$])(eval|Function)\s*\(|new Function\s*\(/g)) {
    const around = code.slice(Math.max(0, m.index - 40), m.index + 60)
    if (!allowed.some((re) => re.test(around))) problems.push(`${file.slice(dist.length + 1)}: ${m[0]} near "${around.replace(/\s+/g, " ")}"`)
  }
}

if (problems.length) {
  console.error(problems.join("\n"))
  process.exit(1)
}
console.log("dist is CSP-clean")
