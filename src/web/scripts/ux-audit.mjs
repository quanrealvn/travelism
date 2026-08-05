/**
 * UX audit: drives the real app in a real browser and reports what is wrong.
 *
 * Unit tests answer "does this component do what it says". They cannot answer
 * "is the tab bar sitting on top of the header", "did the map load any tiles",
 * or "can this button actually be pressed" — all three of which shipped and
 * were caught only here.
 *
 * Usage:
 *   npm run build && dotnet run --project ../WeGo.Api   # serve the built app
 *   node scripts/ux-audit.mjs [outputDir]
 *
 * Exits non-zero when it finds problems, so it can gate a change.
 */
import { chromium } from 'playwright'
import { mkdirSync } from 'node:fs'

const BASE = process.env.WEGO_URL ?? 'http://localhost:5080'
const OUT = process.argv[2] ?? './ux'
mkdirSync(OUT, { recursive: true })

/*
 * 320 is the floor the whole industry still designs to — an iPhone SE, a
 * Galaxy A in display-zoom mode — and it is where a layout that merely "works
 * on mobile" at 390 falls apart. The other three are the sizes this app was
 * actually composed for.
 */
const VIEWPORTS = [
  { name: 'small', width: 320, height: 568 },
  { name: 'mobile', width: 390, height: 844 },
  { name: 'tablet', width: 820, height: 1180 },
  { name: 'desktop', width: 1440, height: 900 },
]

const problems = []

const api = (path, cookie, body, method = 'POST') =>
  fetch(`${BASE}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(cookie ? { Cookie: cookie } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  })

/** A trip that looks like real use: mixed categories, mixed agreement, money owed both ways. */
async function seed() {
  const res = await api('/trips', null, {
    name: 'Mộc Châu 3 ngày 2 đêm',
    destination: 'Mộc Châu, Sơn La',
    startDate: '2026-08-10',
    endDate: '2026-08-12',
    ownerDisplayName: 'Quân',
    budgetAmount: 4000000,
  })
  const cookie = res.headers.get('set-cookie').split(';')[0]
  const { trip, session } = await res.json()

  // No cookie: Linh is a second person on a second browser, not this one.
  const joinRes = await api('/trips/join', null, {
    inviteCode: trip.inviteCode,
    displayName: 'Linh',
  })
  const { session: linh } = await joinRes.json()
  const linhCookie = joinRes.headers.get('set-cookie').split(';')[0]

  const places = [
    ['Thác Dải Yếm', 20.817975, 104.591686, 'Sight', 90, 50000, ['Morning', 'Afternoon'],
      'Thác đẹp nhất Mộc Châu. Đi buổi sáng thì mát và ít người, mang dép chống trượt vì đá rất trơn.',
      [{ url: 'https://vnexpress.net/thac-dai-yem-4123456.html', label: 'Bài VnExpress' }]],
    ['Đồi chè trái tim', 20.891, 104.687, 'Photo', 60, 0, ['Morning'],
      'Chụp ảnh đẹp nhất lúc sương sớm, khoảng 6–7h.', []],
    ['Quán phở Cường', 20.845, 104.628, 'Food', 45, 40000, ['Morning'], null, []],
    ['Rừng thông bản Áng', 20.855, 104.665, 'Sight', 120, 30000, ['Afternoon'], null, []],
    ['Homestay Mùa', 20.852, 104.651, 'Rest', 600, 800000, ['Evening'],
      'Đã đặt phòng, nhận phòng sau 14h.', [{ url: 'https://www.facebook.com/homestaymua', label: null }]],
    ['Chợ đêm Mộc Châu', 20.862, 104.641, 'Other', 90, 150000, ['Evening'], null, []],
  ]

  const ids = []
  for (const [name, lat, lng, category, mins, cost, slots, description, references] of places) {
    const r = await api(`/trips/${trip.id}/places`, cookie, {
      name, lat, lng, category,
      timeSlots: slots,
      estimatedDurationMinutes: mins,
      estimatedCost: cost,
      description,
      references,
    })
    ids.push((await r.json()).id)
  }

  for (const i of [0, 2]) {
    await api(`/trips/${trip.id}/places/${ids[i]}/like`, cookie)
    await api(`/trips/${trip.id}/places/${ids[i]}/like`, linhCookie)
  }
  await api(`/trips/${trip.id}/places/${ids[1]}/like`, cookie)

  await api(`/trips/${trip.id}/itinerary`, cookie, {
    placeId: ids[2], date: '2026-08-10', startTime: '07:30:00',
  })
  await api(`/trips/${trip.id}/itinerary`, cookie, {
    placeId: ids[0], date: '2026-08-10', startTime: '09:00:00', note: 'Mang đồ bơi',
  })
  await api(`/trips/${trip.id}/itinerary`, cookie, {
    placeId: ids[1], date: '2026-08-11', startTime: '06:30:00',
  })

  await api(`/trips/${trip.id}/expenses`, cookie, {
    title: 'Xăng xe cả chuyến', amount: 620000,
    paidByMemberId: session.memberId, date: '2026-08-10',
    category: 'Transport', splitType: 'Equal',
  })
  await api(`/trips/${trip.id}/expenses`, linhCookie, {
    title: 'Homestay 2 đêm', amount: 1600000,
    paidByMemberId: linh.memberId, date: '2026-08-10',
    category: 'Lodging', splitType: 'Equal',
  })
  await api(`/trips/${trip.id}/expenses`, cookie, {
    title: 'Ăn tối chợ đêm', amount: 285000,
    paidByMemberId: session.memberId, date: '2026-08-11',
    category: 'Food', splitType: 'Equal',
  })

  // A second and third trip on the same browser, one already over, so the
  // switcher has both of its sections to render.
  //
  // The cookie has to be carried forward from each response: every create
  // returns a new one holding the accumulated memberships, and reusing the
  // original would leave the browser holding only the first trip.
  let current = cookie
  for (const extra of [
    {
      name: 'Đà Lạt cuối tuần',
      destination: 'Đà Lạt, Lâm Đồng',
      startDate: '2026-12-18',
      endDate: '2026-12-20',
    },
    {
      name: 'Hà Giang mùa hoa',
      destination: 'Hà Giang',
      startDate: '2024-10-04',
      endDate: '2024-10-08',
    },
  ]) {
    const created = await api('/trips', current, { ...extra, ownerDisplayName: 'Quân' })
    current = created.headers.get('set-cookie').split(';')[0]
  }

  return current
}

const cookie = await seed()
const [cookieName, cookieValue] = cookie.split('=')

const browser = await chromium.launch()

for (const vp of VIEWPORTS) {
  const context = await browser.newContext({
    viewport: { width: vp.width, height: vp.height },
    deviceScaleFactor: 2,
    isMobile: vp.name === 'mobile',
    hasTouch: vp.name === 'mobile',
    // The audience is Vietnamese, and <input type="time"> follows the browser
    // locale rather than the page's lang: under the default en-US these render
    // "07:30 AM", which is not what any of these users would see.
    locale: 'vi-VN',
    timezoneId: 'Asia/Ho_Chi_Minh',
  })
  await context.addCookies([
    { name: cookieName, value: cookieValue, domain: 'localhost', path: '/' },
  ])

  const page = await context.newPage()
  page.on('pageerror', (e) => problems.push(`[${vp.name}] pageerror: ${e}`))
  page.on('console', (m) => m.type() === 'error' && problems.push(`[${vp.name}] console: ${m.text()}`))

  await page.goto(BASE, { waitUntil: 'networkidle' })
  await page.waitForTimeout(2500)

  const tabs = [
    ['wishlist', /wishlist/i],
    ['itinerary', /lịch trình/i],
    ['money', /chi tiêu/i],
  ]

  for (const [key, pattern] of tabs) {
    const tab = page.getByRole('tab', { name: pattern })
    if (await tab.count()) {
      await tab.first().click()
      await page.waitForTimeout(900)
    }
    await shoot(page, vp, key)
    await audit(page, `${vp.name}/${key}`)

    // The map pane and the sheets are reachable only by interaction, so a tab
    // screenshot alone would never show them — and the forms live in sheets,
    // so auditing only the tabs would leave every form unchecked.
    if (key === 'wishlist') {
      const mapTab = page.getByRole('button', { name: 'Bản đồ', exact: true })
      if (await mapTab.count()) {
        await mapTab.first().click()
        /*
         * Wait for the tiles themselves, not for a guess at how long they take.
         *
         * Leaflet only re-measures once the pane is visible, and only then asks
         * for the tiles that cover the real size — so a fixed delay raced the
         * OSM tile server and reported a half-drawn map as a broken one. Three
         * successive runs said 1, 2 and 7 tiles for the same layout, which is
         * the signature of a race rather than a defect.
         *
         * Still bounded: if the tiles genuinely never arrive this falls through
         * and the check below reports it, which is the case worth catching.
         */
        await page
          .waitForFunction(
            () => {
              const map = document.querySelector('.leaflet-container')
              if (!map) return false
              const r = map.getBoundingClientRect()
              const needed = Math.ceil(r.width / 256) * Math.ceil(r.height / 256)
              return map.querySelectorAll('.leaflet-tile-loaded').length >= needed * 0.6
            },
            { timeout: 15000 },
          )
          .catch(() => undefined)
        await page.waitForTimeout(600)
        await shoot(page, vp, 'map')
        await audit(page, `${vp.name}/map`)
        await page.getByRole('button', { name: 'Danh sách', exact: true }).first().click()
        await page.waitForTimeout(400)
      }

      await inSheet(page, vp, 'add-place', /thêm địa điểm/i)
      await inSheet(page, vp, 'trip-sheet', /thông tin.*mã mời/i)

      // A card opens in place, and the map is one more tap from inside it.
      const firstPlace = page.locator('.place-head').first()
      if (await firstPlace.count()) {
        await firstPlace.click()
        await page.waitForTimeout(600)
        await shoot(page, vp, 'selected')
        await audit(page, `${vp.name}/selected`)

        if ((await page.locator('.place.is-open .place-body').count()) === 0) {
          problems.push(`[${vp.name}] tapping a place did not open it`)
        }

        // On a phone the map is a separate pane, so the open card has to offer
        // a way to it — selecting alone points a map nobody is looking at.
        if (vp.name === 'mobile') {
          const toMap = page.getByRole('button', { name: /bản đồ/i })
          if ((await toMap.count()) === 0) {
            problems.push(`[${vp.name}] an open card offers no way to the map`)
          } else {
            await toMap.first().click()
            await page.waitForTimeout(1500)
            if ((await page.locator('.leaflet-container').count()) === 0) {
              problems.push(`[${vp.name}] the map did not appear from the open card`)
            }
            const back = page.getByRole('button', { name: 'Danh sách', exact: true })
            if (await back.count()) await back.first().click()
            await page.waitForTimeout(400)
          }
        }

        await firstPlace.click()
        await page.waitForTimeout(300)
      }
    }

    if (key === 'money') {
      await inSheet(page, vp, 'add-expense', /thêm khoản chi/i)
    }
  }

  // The switcher is a menu: the trips it lists, the way to start another, and
  // the way through to the full screen. Both layers get audited.
  const switcher = page.getByRole('button', { name: /đổi chuyến đi/i })
  if (await switcher.count()) {
    await switcher.first().click()
    await page.waitForTimeout(700)

    const listed = await page.locator('.trip-menu-item').count()
    if (listed < 3) {
      problems.push(`[${vp.name}] the switcher menu lists ${listed} trips, expected 3`)
    }
    await shoot(page, vp, 'trip-menu')
    await audit(page, `${vp.name}/trip-menu`)

    await page.getByRole('menuitem', { name: /xem tất cả chuyến đi/i }).click()
    await page.waitForTimeout(900)
    await shoot(page, vp, 'trips')
    await audit(page, `${vp.name}/trips`)
    const upcoming = await page.locator('.trip-card').count()
    if (upcoming < 3) {
      problems.push(`[${vp.name}] trips screen shows ${upcoming} trips, expected 3`)
    }
  } else {
    problems.push(`[${vp.name}] no way to reach the other trips this browser holds`)
  }

  await context.close()
}

// A spinner has to be visible while something is genuinely in flight.
{
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 },
    deviceScaleFactor: 2,
    isMobile: true,
    locale: 'vi-VN',
  })
  await context.addCookies([
    { name: cookieName, value: cookieValue, domain: 'localhost', path: '/' },
  ])
  const page = await context.newPage()
  await page.route('**/places', async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 4000))
    await route.continue()
  })
  await page.goto(BASE)
  await page.waitForTimeout(1200)
  await page.screenshot({ path: `${OUT}/mobile-loading.png` })
  if ((await page.locator('.spinner').count()) === 0) {
    problems.push('[mobile/loading] no spinner while the trip is loading')
  }
  await context.close()
}

await browser.close()
console.log(JSON.stringify({ problems }, null, 2))
process.exit(problems.length === 0 ? 0 : 1)

function shoot(page, vp, key) {
  return page.screenshot({
    path: `${OUT}/${vp.name}-${key}.png`,
    fullPage: vp.name === 'mobile',
  })
}

/** Opens a sheet by the control that summons it, audits it, and closes it. */
async function inSheet(page, vp, key, name) {
  const opener = page.getByRole('button', { name }).first()
  if (!(await opener.count())) {
    problems.push(`[${vp.name}] nothing opens the ${key} sheet`)
    return
  }

  await opener.click()
  await page.waitForTimeout(700)
  await page.screenshot({ path: `${OUT}/${vp.name}-${key}.png` })
  await audit(page, `${vp.name}/${key}`)
  await page.keyboard.press('Escape')
  await page.waitForTimeout(400)
}

/** Objective checks a screenshot cannot show, run against whatever is on screen. */
async function audit(page, where) {
  const result = await page.evaluate(() => {
    const out = {
      horizontalOverflow: false,
      brokenMap: null,
      tinyTargets: [],
      smallText: [],
      overlaps: [],
      lowContrast: [],
      clipped: [],
      unlabelled: [],
      hugeIcons: [],
    }

    const name = (el) => `${el.tagName.toLowerCase()}.${el.className || '-'}`.slice(0, 60)

    out.horizontalOverflow =
      document.documentElement.scrollWidth > document.documentElement.clientWidth + 1

    const controls = [...document.querySelectorAll('button, a, input, select, textarea')].filter(
      (el) => {
        const r = el.getBoundingClientRect()
        if (r.width === 0 || r.height === 0) return false
        // A control hidden behind a label that is itself the target — a chip
        // whose whole body toggles its checkbox. The label is measured instead.
        const s = getComputedStyle(el)
        return !(s.opacity === '0' || s.clipPath.includes('inset(50%)'))
      },
    )

    for (const el of controls) {
      const r = el.getBoundingClientRect()
      // 44px is the comfortable minimum; 40 is the floor this app allows for
      // dense secondary controls, so anything under it is a real defect.
      // Leaflet's attribution is exempt: it is a licence notice that has to be
      // present and unobtrusive, not a control anybody aims at.
      const isAttribution = el.closest('.leaflet-control-attribution') !== null
      if (!isAttribution && (r.height < 40 || r.width < 32)) {
        out.tinyTargets.push(`${name(el)}: ${Math.round(r.width)}x${Math.round(r.height)}`)
      }

      // An icon-only control with no accessible name is invisible to a screen
      // reader and unguessable to everyone else.
      const label =
        el.getAttribute('aria-label') ?? el.textContent?.trim() ?? el.getAttribute('title') ?? ''
      if (label === '' && el.tagName !== 'INPUT' && el.tagName !== 'SELECT') {
        out.unlabelled.push(name(el))
      }

      // An SVG with no intrinsic size stretches to fill its flex parent. The
      // "new trip" button shipped as a 400px purple square with a giant plus
      // in it, and no measurement of the button itself would have said so.
      for (const icon of el.querySelectorAll('svg')) {
        const box = icon.getBoundingClientRect()
        if (box.height > 40 || box.width > 40) {
          out.hugeIcons.push(
            `${name(el)} has a ${Math.round(box.width)}x${Math.round(box.height)} icon`,
          )
        }
      }
    }

    /*
     * Can this control actually be pressed?
     *
     * Comparing every pair of rectangles reported a FAB for floating and a tab
     * bar for having content scroll beneath it, which is what both are for.
     * The question worth asking is whether the point a finger lands on belongs
     * to the control — that is what elementFromPoint answers, and it is how the
     * tab bar sitting invisibly on top of the header was caught.
     *
     * Only the topmost layer is checked. While a sheet is open the chrome
     * behind it is supposed to be unreachable — that is what makes it modal —
     * so auditing the whole page would report the feature as the fault.
     */
    /** The nearest thing that could scroll this element into view, if any. */
    const scrollableAncestor = (el) => {
      for (let node = el.parentElement; node; node = node.parentElement) {
        const overflow = getComputedStyle(node).overflowY
        if (
          (overflow === 'auto' || overflow === 'scroll') &&
          node.scrollHeight - node.clientHeight > 8
        ) {
          return node
        }
      }

      const root = document.documentElement
      return root.scrollHeight - root.clientHeight > 8 ? root : null
    }

    /*
     * An open menu covers what is behind it on purpose, exactly as a sheet
     * does, so while one is up only the menu and its own trigger are in play.
     */
    const menu = document.querySelector('[role="menu"]')
    const overlay =
      document.querySelector('.sheet-panel') ??
      (menu ? (menu.closest('.trip-switcher') ?? menu) : null)

    const reachable = overlay
      ? controls.filter((el) => overlay.contains(el))
      : controls.filter((el) => !el.classList.contains('sheet-backdrop'))

    for (const el of reachable) {
      const r = el.getBoundingClientRect()
      const x = r.left + r.width / 2
      const y = r.top + r.height / 2
      if (x < 0 || y < 0 || x > window.innerWidth || y > window.innerHeight) continue

      const hit = document.elementFromPoint(x, y)
      if (!hit || el.contains(hit) || hit.contains(el)) continue

      /*
       * A floating button covering a list item is the cost of the pattern, not
       * a defect: a nudge of the scroll wheel frees it. It is only a defect
       * when nothing can move it — the element is pinned itself, or nothing
       * containing it scrolls, which is how the map's attribution ended up
       * permanently under the FAB.
       *
       * "Nothing containing it" and not "the page": a form inside a sheet
       * scrolls in the sheet's own body while the document behind does not.
       */
      const pinned = ['fixed', 'sticky'].includes(getComputedStyle(el).position)
      if (!pinned && scrollableAncestor(el)) continue

      out.overlaps.push(`${name(el)} cannot be pressed — ${name(hit)} is on top`)
    }

    const luminance = (rgb) => {
      const [r, g, b] = rgb.map((v) => {
        const s = v / 255
        return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
      })
      return 0.2126 * r + 0.7152 * g + 0.0722 * b
    }

    // Chrome resolves color-mix() to `color(srgb 0.98 0.97 0.95 / 0.88)`, whose
    // channels run 0–1 rather than 0–255. Reading those as 8-bit values makes
    // every such background look black and reports a false contrast failure.
    const parse = (colour) => {
      const parts = (colour.match(/[\d.]+/g) ?? []).map(Number)
      if (parts.length < 3) return null
      const rgb = colour.startsWith('color(')
        ? parts.slice(0, 3).map((v) => v * 255)
        : parts.slice(0, 3)
      return { rgb, alpha: parts.length > 3 ? parts[3] : 1 }
    }

    /*
     * The effective background: walk up until something is not transparent.
     *
     * Returns null when a gradient or image is hit on the way — the balance
     * card is a violet gradient, and sampling only its (transparent) background
     * colour made white-on-violet look like white-on-white and report 1.06:1.
     * Reporting nothing beats reporting a number that is wrong.
     */
    const backdrop = (el) => {
      for (let node = el; node; node = node.parentElement) {
        const style = getComputedStyle(node)
        if (style.backgroundImage !== 'none') return null
        const bg = parse(style.backgroundColor)
        if (bg && bg.alpha > 0.5) return bg.rgb
      }
      return [255, 255, 255]
    }

    const isVisuallyHidden = (el) => {
      const s = getComputedStyle(el)
      return s.clipPath.includes('inset(50%)') || el.classList.contains('visually-hidden')
    }

    for (const el of document.querySelectorAll('*')) {
      if (!el.textContent?.trim() || el.children.length) continue
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') continue
      if (isVisuallyHidden(el)) continue
      // aria-hidden text is decoration by declaration — a map pin's emoji, a
      // tick glyph. It carries no meaning to lose to poor contrast.
      if (el.closest('[aria-hidden="true"]')) continue
      const rect = el.getBoundingClientRect()
      if (rect.width === 0 || rect.height === 0) continue

      const size = parseFloat(style.fontSize)
      if (size < 12) out.smallText.push(`${name(el)}: ${size}px`)

      const fg = parse(style.color)
      const bg = backdrop(el)
      if (fg && bg) {
        const l1 = luminance(fg.rgb)
        const l2 = luminance(bg)
        const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05)
        // WCAG AA: 3:1 for large text, 4.5:1 for everything else.
        const large = size >= 18.66 || (size >= 14 && parseInt(style.fontWeight, 10) >= 700)
        if (ratio < (large ? 3 : 4.5)) {
          out.lowContrast.push(`${name(el)}: ${ratio.toFixed(2)}:1 at ${size}px`)
        }
      }

      // Text taller than its own box is being cut off.
      if (el.scrollHeight > el.clientHeight + 2 && style.overflow === 'hidden') {
        out.clipped.push(name(el))
      }
    }

    /*
     * A map that has not been told its container grew loads only enough tiles
     * for the size it thinks it is, leaving most of the box blank. Nothing
     * else here notices — the element is the right size, its text is legible,
     * its controls are reachable, and it is simply not a map.
     */
    const map = document.querySelector('.leaflet-container')
    if (map) {
      const r = map.getBoundingClientRect()
      const tiles = map.querySelectorAll('.leaflet-tile-loaded').length
      const needed = Math.ceil(r.width / 256) * Math.ceil(r.height / 256)
      if (r.width > 0 && r.height > 0 && tiles < needed * 0.6) {
        out.brokenMap = `${tiles} tiles loaded for a ${Math.round(r.width)}x${Math.round(
          r.height,
        )} map that needs about ${needed}`
      }
    }

    for (const key of ['tinyTargets', 'smallText', 'overlaps', 'lowContrast', 'clipped', 'unlabelled', 'hugeIcons']) {
      out[key] = [...new Set(out[key])].slice(0, 12)
    }
    return out
  })

  if (result.horizontalOverflow) problems.push(`[${where}] page scrolls horizontally`)
  if (result.brokenMap) problems.push(`[${where}] map is not filling its box: ${result.brokenMap}`)
  for (const t of result.tinyTargets) problems.push(`[${where}] small tap target: ${t}`)
  for (const t of result.smallText) problems.push(`[${where}] text under 12px: ${t}`)
  for (const t of result.overlaps) problems.push(`[${where}] controls overlap: ${t}`)
  for (const t of result.lowContrast) problems.push(`[${where}] low contrast: ${t}`)
  for (const t of result.clipped) problems.push(`[${where}] text clipped: ${t}`)
  for (const t of result.unlabelled) problems.push(`[${where}] control has no name: ${t}`)
  for (const t of result.hugeIcons) problems.push(`[${where}] icon has stretched: ${t}`)
}
