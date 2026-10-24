import { beforeEach, describe, expect, it, vi } from "vitest"

vi.stubGlobal("matchMedia", (q: string) => ({ matches: false, media: q, addEventListener() {}, removeEventListener() {} }))

const { setTheme, cycleTheme, resolvedTheme, getTheme, initTheme } = await import("./theme")

beforeEach(() => {
  localStorage.clear()
  setTheme("system")
})

describe("theme", () => {
  it("persists light/dark under cw-theme and applies data-theme", () => {
    setTheme("dark")
    expect(localStorage.getItem("cw-theme")).toBe("dark")
    expect(document.documentElement.dataset.theme).toBe("dark")
  })

  it("clears storage and attribute for system", () => {
    setTheme("light")
    setTheme("system")
    expect(localStorage.getItem("cw-theme")).toBeNull()
    expect(document.documentElement.dataset.theme).toBeUndefined()
    expect(resolvedTheme()).toBe("light")
  })

  it("cycles between light and dark from the resolved value", () => {
    cycleTheme()
    expect(getTheme()).toBe("dark")
    cycleTheme()
    expect(getTheme()).toBe("light")
  })

  it("restores a stored choice on init and ignores junk", () => {
    localStorage.setItem("cw-theme", "dark")
    initTheme()
    expect(getTheme()).toBe("dark")
    localStorage.setItem("cw-theme", "purple")
    initTheme()
    expect(getTheme()).toBe("system")
  })
})
