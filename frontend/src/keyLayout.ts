export type Key = { label: string; vk: number; width?: number }
export type Assignment = { key: number; preserve: boolean }
export type Bindings = Record<number, Assignment>
export type Layer = { id: string; name: string; key: number; bindings: Bindings }
export type LayerProfile = { base: Bindings; layers: Layer[] }
const initial = [{ id: 'caps', name: 'Caps', key: 20 }, { id: 'fn1', name: 'Fn1', key: 250 }, { id: 'fn2', name: 'Fn2', key: 251 }]

const key = (label: string, vk: number, width?: number): Key => ({ label, vk, width })
const letters = (text: string) => [...text].map(label => key(label, label.charCodeAt(0)))

export const rows: Key[][] = [
  [key('Esc', 27), ...letters('1234567890'), key('−', 189), key('=', 187), key('Backspace', 8, 1.75)],
  [key('Tab', 9, 1.5), ...letters('QWERTYUIOP'), key('[', 219), key(']', 221), key('\\', 220, 1.25)],
  [key('Caps', 20, 1.75), ...letters('ASDFGHJKL'), key(';', 186), key("'", 222), key('Enter', 13, 2)],
  [key('Shift', 160, 2.25), ...letters('ZXCVBNM'), key(',', 188), key('.', 190), key('/', 191), key('Shift', 161, 2.5)],
  [key('Ctrl', 162, 1.25), key('Win', 91, 1.25), key('Alt', 164, 1.25), key('Space', 32, 6.5),
    key('Alt', 165, 1.25), key('Fn1', 250, 1.25), key('Fn2', 251, 1.25), key('Ctrl', 163, 1.25)],
]
export const layoutKeys = rows.flat()
export const modifiers = new Set([20, 91, 160, 161, 162, 163, 164, 165, 250, 251])
const modifierLabels: Record<number, string> = { 160: 'Shift L', 161: 'Shift R', 162: 'Ctrl L', 163: 'Ctrl R', 164: 'Alt L', 165: 'Alt R' }
export function keyName(vk: number) { return modifierLabels[vk] ?? layoutKeys.find(item => item.vk === vk)?.label ?? '' }

export function hardwareLayerKey(pressed: number[]) {
  return pressed.includes(251) ? 251 : pressed.includes(250) ? 250 : 0
}

export function heldLayer(profile: LayerProfile, pressed: number[]) {
  const fn = hardwareLayerKey(pressed)
  if (fn) return profile.layers.find(layer => layer.key === fn)
  return [...profile.layers].reverse().find(layer => layer.key && pressed.includes(layer.key))
}

export function layerLayout(profile: LayerProfile, layer: Layer | undefined, language: 'ru' | 'en', physicalFn = 0) {
  const activator = physicalFn || layer?.key || 0
  const factory = factoryShortcuts(activator, language)
  return {
    bindings: physicalFn ? undefined : layer?.bindings ?? profile.base,
    inherited: layer && !factory ? profile.base : undefined,
    factory,
    activator,
    layerKeys: Object.fromEntries(profile.layers.filter(item => item.key).map(item => [item.key, item.name])),
  }
}

function factoryShortcuts(vk: number, language: 'ru' | 'en'): Record<number, string> | undefined {
  const ru = language === 'ru'
  // Verified model manual labels. Ambiguous entries are omitted.
  if (vk === 250) return {
    27: '`', 49: 'F1', 50: 'F2', 51: 'F3', 52: 'F4', 53: 'F5', 54: 'F6', 55: 'F7',
    81: 'BT 1', 87: 'BT 2', 69: 'BT 3', 82: '2.4G', 91: 'Win Lock',
    85: 'Home', 73: 'End', 79: 'Page Up', 188: 'Ins', 191: 'Del', 32: ru ? 'Заряд' : 'Battery',
  }
  if (vk === 251) return {
    56: ru ? 'Режим' : 'Effect', 57: ru ? 'Цвет' : 'Color', 48: 'RGB On/Off',
    189: ru ? 'Яркость −' : 'Light −', 187: ru ? 'Яркость +' : 'Light +',
    8: ru ? 'Свой RGB' : 'Custom RGB', 20: ru ? 'Сброс 5 с' : 'Reset 5 s',
  }
}
const fKeys = Array.from({ length: 24 }, (_, index) => key(`F${index + 1}`, 112 + index))
export const targetMain = [
  [key('Esc', 27), ...fKeys.slice(0, 12)],
  [key('`', 192), ...rows[0].slice(1)],
  rows[1], rows[2], rows[3],
  [key('Ctrl L', 162, 1.25), key('Win L', 91, 1.25), key('Alt L', 164, 1.25), key('Space', 32, 6.5),
    key('Alt R', 165, 1.25), key('Win R', 92, 1.25), key('Menu', 93, 1.25), key('Ctrl R', 163, 1.25)],
]
export const targetNavigation = [
  [key('PrtSc', 44), key('Scroll', 145), key('Pause', 19)],
  [key('Ins', 45), key('Home', 36), key('PgUp', 33)],
  [key('Del', 46), key('End', 35), key('PgDn', 34)],
  [key('↑', 38)], [key('←', 37), key('↓', 40), key('→', 39)],
]
export const targetNumpad = [
  [key('Num', 144), key('/', 111), key('*', 106), key('−', 109)],
  [key('7', 103), key('8', 104), key('9', 105), key('+', 107)],
  [key('4', 100), key('5', 101), key('6', 102)],
  [key('1', 97), key('2', 98), key('3', 99), key('Enter', 257)],
  [key('0', 96, 2), key('.', 110)],
]
export const targetMedia = [
  key('Mute', 173), key('Vol −', 174), key('Vol +', 175), key('Prev', 177), key('Play / Pause', 179),
  key('Next', 176), key('Stop', 178), key('Browser Back', 166), key('Browser Forward', 167),
  key('Refresh', 168), key('Search', 170), key('Favorites', 171), key('Browser Home', 172),
  key('Mail', 180), key('Media', 181), key('App 1', 182), key('App 2', 183),
]
export const targetExtra = [...fKeys.slice(12), key('Esc → ` / ~', 256)]
const outputKeys = [
  ...targetMain.flat(), ...targetNavigation.flat(), ...targetNumpad.flat().map(item => ({ ...item, label: `Num ${item.label}` })),
  ...targetMedia, ...targetExtra,
]
const outputLabels = new Map(outputKeys.map(item => [item.vk, item.label]))
export function actionLabel(code: number) { return modifierLabels[code] ?? outputLabels.get(code) ?? '' }

function capsPreset(): Bindings {
  const bindings: Bindings = {}
  ;[49, 50, 51, 52, 53, 54, 55, 56, 57, 48, 189, 187].forEach((vk, index) => {
    bindings[vk] = { key: 112 + index, preserve: false }
  })
  for (const [vk, target, preserve] of [
    [219, 36, 0], [221, 35, 0], [186, 174, 0], [222, 175, 0], [76, 173, 0],
    [188, 177, 0], [190, 176, 0], [191, 46, 0], [80, 44, 1],
    [87, 38, 1], [65, 37, 1], [83, 40, 1], [68, 39, 1], [27, 256, 1],
  ]) bindings[vk] = { key: target, preserve: Boolean(preserve) }
  return bindings
}

function readBindings(value: unknown, fallback: Bindings = {}): Bindings {
  if (!value || typeof value !== 'object') return fallback
  const saved = value as Bindings
  const bindings: Bindings = {}
  for (const { vk } of layoutKeys) {
    const action = saved[vk]
    if (!modifiers.has(vk) && action && outputLabels.has(action.key) && typeof action.preserve === 'boolean')
      bindings[vk] = { key: action.key, preserve: action.preserve }
  }
  return bindings
}

export function loadLayerProfile(): LayerProfile {
  const defaults = () => initial.map(layer => ({ ...layer, bindings: layer.id === 'caps' ? capsPreset() : {} }))
  try {
    const saved = JSON.parse(localStorage.getItem('caraxes.layers.v3') ?? 'null')
    const ids = new Set<string>(), used = new Set<number>()
    const validKey = (vk: unknown): vk is number => vk === 0 || layoutKeys.some(item => item.vk === vk)
    if (Array.isArray(saved?.layers)) {
      const layers: Layer[] = []
      for (const item of saved.layers.slice(0, layoutKeys.length)) {
        if (!item || typeof item.id !== 'string' || !item.id || ids.has(item.id) || typeof item.name !== 'string' || !item.name.trim()) continue
        const key = validKey(item.key) && !used.has(item.key) ? item.key : 0
        ids.add(item.id)
        if (key) used.add(key)
        layers.push({ id: item.id, name: item.name.trim().slice(0, 48), key, bindings: readBindings(item.bindings) })
      }
      return { base: readBindings(saved.base), layers }
    }
    const v2 = JSON.parse(localStorage.getItem('caraxes.layers.v2') ?? 'null')
    const previous = v2?.bindings ?? JSON.parse(localStorage.getItem('caraxes.layers.v1') ?? 'null')
    const layers = defaults().map(layer => {
      const value = v2?.activators?.[layer.id]
      const key = validKey(value) ? value : layer.key
      const activator = used.has(key) ? 0 : key
      if (activator) used.add(activator)
      return { ...layer, key: activator, bindings: readBindings(previous?.[layer.id], layer.bindings) }
    })
    return { base: readBindings(previous?.base), layers }
  } catch { /* A malformed stored draft falls back to the built-in profile. */ }
  return { base: {}, layers: defaults() }
}

export function runtimeProfile(profile: LayerProfile) {
  return { base: profile.base, layers: profile.layers.map(({ key, bindings }) => ({ key, bindings })) }
}
