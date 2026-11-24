export interface DeviceState {
  connection: 'disconnected' | 'usb' | 'unavailable'
  receiverPresent?: boolean
  generation: number
  brightness: number | null
  previous: number | null
  issue: string | null
  canvasAvailable?: boolean
  lightingRecoveryAvailable?: boolean
  lightingUndoAvailable?: boolean
  effect?: string | null
}

type Bridge = {
  postMessage(message: unknown): void
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void
}

const native = (window as Window & { chrome?: { webview?: Bridge } }).chrome?.webview
export const desktop = Boolean(native)
let sequence = 0
interface Reply { state: DeviceState; error?: string; layersActive?: boolean; ignoreTestOutput?: boolean; receiverReport?: string | null; startupEnabled?: boolean }
const pending = new Map<string, (reply: Reply) => void>()
const listeners = new Set<(state: DeviceState) => void>()
const capsLockListeners = new Set<(enabled: boolean) => void>()
const keyListeners = new Set<(keys: number[]) => void>()
const transitionListeners = new Set<(vk: number, down: boolean) => void>()

native?.addEventListener('message', ({ data }) => {
  if (!data || typeof data !== 'object') return
  if (data.event === 'keys' && Array.isArray(data.keys)) {
    keyListeners.forEach(listener => listener(data.keys))
    const transition = data.transition
    if (Number.isInteger(transition?.vk) && typeof transition.down === 'boolean')
      transitionListeners.forEach(listener => listener(transition.vk, transition.down))
    return
  }
  if (!data.state) return
  if (typeof data.capsLockOn === 'boolean') capsLockListeners.forEach(listener => listener(data.capsLockOn))
  if (data.event === 'state') {
    listeners.forEach(listener => listener(data.state))
    return
  }
  pending.get(data.id)?.(data)
})

export function onState(listener: (state: DeviceState) => void) {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

export function onCapsLock(listener: (enabled: boolean) => void) {
  capsLockListeners.add(listener)
  return () => { capsLockListeners.delete(listener) }
}

export function onKeys(listener: (keys: number[]) => void) {
  keyListeners.add(listener)
  return () => { keyListeners.delete(listener) }
}

export function onKeyTransition(listener: (vk: number, down: boolean) => void) {
  transitionListeners.add(listener)
  return () => { transitionListeners.delete(listener) }
}

export function request(method: 'state' | 'refresh' | 'receiver.read' | 'ui.language' | 'brightness.set' | 'brightness.restore' | 'lighting.recover' | 'lighting.undoRecovery' | 'layers.start' | 'layers.stop' | 'layers.preview' | 'layers.editCaps' | 'caps.set' | 'startup.get' | 'startup.set' | 'keys.test.output' | 'canvas.preview' | 'effect.start' | 'effect.stop', payload: object = {}) {
  return new Promise<Reply>((resolve, reject) => {
    if (!native) { reject(new Error('desktop')); return }
    const id = `caraxes-${++sequence}`
    const finish = () => { clearTimeout(timer); pending.delete(id) }
    const timer = window.setTimeout(() => { finish(); reject(new Error('timeout')) }, 15000)
    pending.set(id, reply => { finish(); resolve(reply) })
    try { native.postMessage({ id, method, payload }) }
    catch (error) { finish(); reject(error) }
  })
}
