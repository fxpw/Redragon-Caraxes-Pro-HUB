<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { desktop, onKeys, onState, request, type DeviceState } from './bridge'
import { loadLayerProfile, rows, runtimeProfile } from './keyLayout'
import Layers from './Layers.vue'
import RgbCanvas from './RgbCanvas.vue'
import KeyTester from './KeyTester.vue'
import LightingModes from './LightingModes.vue'

const strings = {
  ru: {
    connected: 'USB подключён', disconnected: 'USB не подключён', unavailable: 'USB недоступен', receiver: 'Приёмник 2,4 ГГц подключён',
    checking: 'Проверяю USB', lighting: 'Подсветка', brightness: 'Яркость',
    actual: 'На клавиатуре', refresh: 'Перечитать', apply: 'Применить', undo: 'Вернуть',
    connect: 'Подключите Caraxes Pro по кабелю в проводном режиме.',
    read: 'Яркость считана с клавиатуры.', applied: 'Запись подтверждена чтением с клавиатуры.',
    restored: 'Предыдущее значение восстановлено и проверено.', retry: 'Перечитайте настройки по USB.',
    desktop: 'Для доступа к клавиатуре откройте настольный HUB.',
    layout: 'Раскладка клавиатуры Caraxes Pro', change: 'Уровень яркости',
    error: 'Операция не подтверждена', timeout: 'HUB не ответил. Перечитайте состояние.',
    unsupportedEffect: 'Редактирование яркости проверено для эффекта «Цветение» (09).',
    rollbackUnverified: 'Возврат настроек не подтверждён. Перечитайте клавиатуру.',
    storedLevel: 'Сохранённый уровень',
    recoverLighting: 'Восстановить «Цветение»', undoLighting: 'Вернуть прежний режим',
    recoveryAvailable: 'Можно вернуть подсветку в режиме «Цветение».',
    layers: 'Слои', test: 'Тест клавиш', settings: 'Настройки',
    startup: 'Запускать HUB при входе в Windows', startupNote: 'При входе HUB запускается в трее.',
    startupError: 'Не удалось проверить настройку автозапуска.',
    receiverRead: 'Прочитать отчёт приёмника',
    receiverNote: 'Приёмник передаёт нажатия в Windows. Для чтения и записи RGB нужен кабель.',
    receiverReport: 'Последний ответ приёмника', receiverError: 'Не удалось прочитать отчёт приёмника.',
  },
  en: {
    connected: 'USB connected', disconnected: 'USB disconnected', unavailable: 'USB unavailable', receiver: '2.4 GHz receiver connected',
    checking: 'Checking USB', lighting: 'Lighting', brightness: 'Brightness',
    actual: 'On keyboard', refresh: 'Read again', apply: 'Apply', undo: 'Restore',
    connect: 'Connect the Caraxes Pro by cable in wired mode.',
    read: 'Brightness read from the keyboard.', applied: 'Write verified by reading from the keyboard.',
    restored: 'Previous value restored and verified.', retry: 'Read the settings over USB again.',
    desktop: 'Open the desktop HUB to access the keyboard.',
    layout: 'Caraxes Pro keyboard layout', change: 'Brightness level',
    error: 'Operation could not be verified', timeout: 'The HUB did not respond. Read the device again.',
    unsupportedEffect: 'Brightness editing is verified for the Blossoming effect (09).',
    rollbackUnverified: 'Rollback could not be verified. Read the keyboard again.',
    storedLevel: 'Stored level',
    recoverLighting: 'Restore Blossoming', undoLighting: 'Restore previous mode',
    recoveryAvailable: 'The Blossoming effect can restore the backlight.',
    layers: 'Layers', test: 'Key test', settings: 'Settings',
    startup: 'Start HUB when I sign in to Windows', startupNote: 'HUB starts in the notification area.',
    startupError: 'Could not verify the startup setting.',
    receiverRead: 'Read receiver report',
    receiverNote: 'The receiver sends keystrokes to Windows. Reading and writing RGB settings requires a cable.',
    receiverReport: 'Last receiver response', receiverError: 'Could not read the receiver report.',
  },
}
const language = ref<'ru' | 'en'>(localStorage.getItem('caraxes.locale') === 'en' ? 'en' : 'ru')
const t = computed(() => strings[language.value])
const page = ref<'lighting' | 'layers' | 'test' | 'settings'>('lighting')
const layerProfile = ref(loadLayerProfile())
const pressed = ref<number[]>([])
onUnmounted(onKeys(keys => { pressed.value = keys }))
watch(layerProfile, profile => localStorage.setItem('caraxes.layers.v3', JSON.stringify(profile)), { deep: true })
const editorReservesCaps = computed(() => page.value === 'layers' && layerProfile.value.layers.some(layer => layer.key === 20))
watch(editorReservesCaps, reserved => {
  if (desktop) void request('layers.editCaps', { reserved }).catch(() => {})
})
function blockEditorCaps(event: KeyboardEvent) {
  if (editorReservesCaps.value && event.code === 'CapsLock') {
    event.preventDefault()
    event.stopPropagation()
  }
}
onMounted(() => {
  window.addEventListener('keydown', blockEditorCaps, true)
  window.addEventListener('keyup', blockEditorCaps, true)
})
onUnmounted(() => {
  window.removeEventListener('keydown', blockEditorCaps, true)
  window.removeEventListener('keyup', blockEditorCaps, true)
})
watch(() => page.value === 'test' ? JSON.stringify(runtimeProfile(layerProfile.value)) : '', async value => {
  if (!desktop) return
  try { await request('layers.preview', value ? JSON.parse(value) : {}) }
  catch { /* The active tester still reports the physical events it receives. */ }
})
function setLanguage(value: 'ru' | 'en') {
  language.value = value
  document.documentElement.lang = value
  localStorage.setItem('caraxes.locale', value)
  if (desktop) void request('ui.language', { language: value }).catch(() => {})
}

const state = ref<DeviceState>({ connection: 'disconnected', generation: 0, brightness: null, previous: null, issue: null })
const initial = ref(desktop)
const pending = ref(false)
const draft = ref(0)
const feedback = ref<'applied' | 'restored' | 'timeout' | 'error' | null>(null)
const details = ref<string | null>(null)
const receiverReport = ref<string | null>(null)
const receiverError = ref(false)
const startup = ref<boolean | null>(null)
const startupPending = ref(false)
const startupError = ref(false)
const status = computed(() => initial.value ? t.value.checking
  : t.value[state.value.connection === 'usb' ? 'connected' : state.value.receiverPresent ? 'receiver' : state.value.connection])
const editable = computed(() => state.value.brightness !== null && !pending.value)
const deviceIssue = computed(() => {
  const issue = state.value.issue
  return issue === 'unsupportedEffect' || issue === 'rollbackUnverified' ? t.value[issue] : issue
})
const levels = [0, 1, 2, 3, 4]

async function configureStartup(enabled?: boolean) {
  if (!desktop || startupPending.value) return
  startupPending.value = true
  startupError.value = false
  try {
    const reply = await (enabled === undefined ? request('startup.get') : request('startup.set', { enabled }))
    if (reply.error || typeof reply.startupEnabled !== 'boolean') throw new Error('startup')
    startup.value = reply.startupEnabled
  } catch { startupError.value = true }
  finally { startupPending.value = false }
}
function changeStartup(event: Event) {
  const checkbox = event.target as HTMLInputElement
  const enabled = checkbox.checked
  checkbox.checked = startup.value === true
  void configureStartup(enabled)
}
watch(page, value => { if (value === 'settings') void configureStartup() })

function replaceState(next: DeviceState, force = false) {
  if (next.generation < state.value.generation) return
  if (force || state.value.generation !== next.generation || state.value.brightness !== next.brightness)
    draft.value = next.brightness ?? 0
  if (state.value.generation !== next.generation) {
    details.value = null
    feedback.value = null
  }
  if (!next.receiverPresent) {
    receiverReport.value = null
    receiverError.value = false
  }
  initial.value = false
  state.value = next
}

async function run(method: Parameters<typeof request>[0]) {
  if (!desktop || pending.value) return
  pending.value = true
  feedback.value = null
  details.value = null
  try {
    const payload = method === 'brightness.set' ? { level: draft.value, expected: state.value.brightness, generation: state.value.generation }
      : method === 'brightness.restore' || method === 'lighting.recover' || method === 'lighting.undoRecovery' ? { generation: state.value.generation } : {}
    const reply = await request(method, payload)
    replaceState(reply.state, true)
    if (reply.error) {
      feedback.value = 'error'
      details.value = reply.error
    } else if (method === 'brightness.set' || method === 'lighting.recover') feedback.value = 'applied'
    else if (method === 'brightness.restore' || method === 'lighting.undoRecovery') feedback.value = 'restored'
  } catch {
    initial.value = false
    feedback.value = 'timeout'
  } finally { pending.value = false }
}

async function readReceiver() {
  if (!desktop || pending.value) return
  pending.value = true
  receiverError.value = false
  receiverReport.value = null
  try {
    const reply = await request('receiver.read')
    replaceState(reply.state)
    receiverReport.value = reply.receiverReport ?? null
    receiverError.value = !receiverReport.value
  } catch { receiverError.value = true }
  finally { pending.value = false }
}

const unsubscribe = onState(replaceState)
onMounted(() => { setLanguage(language.value); void run('state') })
onUnmounted(unsubscribe)
</script>

<template>
  <header class="topbar">
    <div class="brand"><span class="brand-mark" aria-hidden="true"><i></i><i></i><i></i></span> Caraxes Pro <span class="hub">HUB</span></div>
    <div class="top-actions">
      <span class="status" :class="{ online: state.connection === 'usb' || state.receiverPresent }" role="status"><i></i>{{ status }}</span>
      <div class="language" aria-label="Language">
        <button :aria-pressed="language === 'ru'" @click="setLanguage('ru')">RU</button>
        <button :aria-pressed="language === 'en'" @click="setLanguage('en')">EN</button>
      </div>
    </div>
  </header>
  <main>
    <nav class="workspace-nav" :aria-label="language === 'ru' ? 'Разделы HUB' : 'HUB sections'">
      <button v-for="destination in (['lighting', 'layers', 'test', 'settings'] as const)" :key="destination"
        :aria-pressed="page === destination" @click="page = destination">{{ t[destination] }}</button>
    </nav>
    <div v-show="page === 'lighting'" class="workspace-content"><LightingModes :language="language" :device="state" @state="replaceState" /></div>
    <section v-show="page === 'lighting'" class="device-card" aria-labelledby="device-title">
      <div class="device-header">
        <div><p class="model">K644CGO-RGB-PRO</p><h1 id="device-title">Redragon Caraxes Pro</h1></div>
        <button v-if="state.connection !== 'usb' && state.receiverPresent" class="refresh" :disabled="pending || !desktop" @click="readReceiver">{{ t.receiverRead }}</button>
        <button v-else class="refresh" :disabled="pending || !desktop" @click="run('refresh')"><span aria-hidden="true">↻</span> {{ t.refresh }}</button>
      </div>
      <div class="keyboard" role="img" :aria-label="t.layout" :style="{ '--light-level': Math.min(state.brightness ?? 0, 4) / 4 }">
        <div v-for="(row, index) in rows" :key="index" class="key-row">
          <span v-for="{ label, width, vk } in row" :key="vk" class="key" :style="{ flexGrow: width ?? 1 }">{{ label }}</span>
        </div>
      </div>
      <p class="hardware" v-if="state.connection === 'usb'">USB <span>258A:0049</span><span>MI_01 · COL05/06</span></p>
      <template v-else-if="state.receiverPresent">
        <p class="hardware">2,4 GHz <span>25A7:FA70</span><span>MI_01 · COL06</span></p>
        <p v-if="receiverReport || receiverError" role="status" class="feedback layer-note" :class="{ problem: receiverError }">
          <template v-if="receiverReport">{{ t.receiverReport }} <code>{{ receiverReport }}</code></template>
          <template v-else>{{ t.receiverError }}</template>
        </p>
      </template>
    </section>
    <section v-show="page === 'lighting'" class="lighting-card" aria-labelledby="lighting-title" :aria-busy="pending">
      <div class="section-head">
        <h2 id="lighting-title">{{ t.lighting }}</h2>
        <p v-if="state.brightness !== null" class="actual">{{ state.brightness > 4 ? t.storedLevel : t.actual }} <strong>{{ state.brightness }}{{ state.brightness <= 4 ? ' / 4' : '' }}</strong></p>
      </div>
      <fieldset :disabled="!editable">
        <legend>{{ t.brightness }}</legend>
        <div class="level-control" :aria-label="t.change">
          <label v-for="level in levels" :key="level" :class="{ selected: draft === level && state.brightness !== null }">
            <input v-model="draft" type="radio" name="brightness" :value="level" />
            <span>{{ level }}</span>
          </label>
        </div>
      </fieldset>
      <div class="lighting-footer">
        <p role="status" class="feedback" :class="{ problem: feedback === 'error' || feedback === 'timeout' || state.issue }">
          {{ feedback ? t[feedback] : !desktop ? t.desktop : initial ? t.checking : state.lightingRecoveryAvailable ? t.recoveryAvailable : state.brightness !== null ? t.read : state.issue === 'unsupportedEffect' ? deviceIssue : state.issue ? t.retry : state.receiverPresent ? t.receiverNote : t.connect }}
          <span v-if="details || (state.issue && state.issue !== 'unsupportedEffect')" class="detail">{{ details ?? deviceIssue }}</span>
        </p>
        <div class="actions">
          <button v-if="state.lightingRecoveryAvailable" class="primary" :disabled="pending" @click="run('lighting.recover')">{{ t.recoverLighting }}</button>
          <button v-if="state.lightingUndoAvailable" :disabled="pending" @click="run('lighting.undoRecovery')">{{ t.undoLighting }}</button>
          <button v-if="state.previous !== null" :disabled="!editable" @click="run('brightness.restore')">{{ t.undo }} {{ state.previous }}</button>
          <button v-if="!state.lightingRecoveryAvailable" class="primary" :disabled="!editable || draft === state.brightness" @click="run('brightness.set')">{{ t.apply }}</button>
        </div>
      </div>
    </section>
    <div v-show="page === 'layers'" class="workspace-content"><Layers :language="language" :profile="layerProfile" :pressed="pressed" /></div>
    <div v-show="page === 'lighting'" class="workspace-content"><RgbCanvas :language="language" :device="state" @state="replaceState" /></div>
    <div v-show="page === 'test'" class="workspace-content"><KeyTester :language="language" :active="page === 'test'" :profile="layerProfile" :pressed="pressed" /></div>
    <section v-show="page === 'settings'" class="device-card workspace-card app-settings" aria-labelledby="settings-title">
      <h1 id="settings-title">{{ t.settings }}</h1>
      <label class="startup-option">
        <input type="checkbox" :checked="startup === true" :disabled="startup === null || startupPending"
          @change="changeStartup" />{{ t.startup }}
      </label>
      <p class="feedback">{{ t.startupNote }}</p>
      <p v-if="startupError" class="feedback problem" role="status">{{ t.startupError }}</p>
    </section>
  </main>
</template>
