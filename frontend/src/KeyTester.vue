<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { desktop, onKeyTransition, request } from './bridge'
import { hardwareLayerKey, heldLayer, keyName, layerLayout, layoutKeys, type LayerProfile } from './keyLayout'
import KeyLayout from './KeyLayout.vue'

const props = defineProps<{ language: 'ru' | 'en'; active: boolean; profile: LayerProfile; pressed: number[] }>()
const tr = (ru: string, en: string) => props.language === 'ru' ? ru : en
const surface = ref<HTMLElement>()
const windowsKey = ref('')
const seen = ref<number[]>([])
type HistoryEvent = { id: number; name: string; down: boolean; time: Date }
const physicalHistory = ref<HistoryEvent[]>([])
const windowsHistory = ref<HistoryEvent[]>([])
let eventId = 0
let session = 0
const byVk = new Map(layoutKeys.map(item => [item.vk, item]))
// The K644 has one physical key for Esc and the firmware's Backquote output.
const physicalKey = (vk: number) => vk === 192 ? 27 : vk
const visualPressed = computed(() => props.pressed.map(physicalKey))
const physicalFn = computed(() => hardwareLayerKey(props.pressed))
const layer = computed(() => heldLayer(props.profile, props.pressed))
const layout = computed(() => layerLayout(props.profile, layer.value, props.language, physicalFn.value))
const stockFn = computed(() => physicalFn.value !== 0 && !layer.value)
const histories = computed(() => [
  { id: 'physical', label: tr('Нажато на клавиатуре', 'Pressed on keyboard'), events: physicalHistory.value },
  { id: 'windows', label: tr('События Windows', 'Windows events'), events: windowsHistory.value },
])

onUnmounted(onKeyTransition((vk, down) => {
  vk = physicalKey(vk)
  const name = byVk.has(vk) ? keyName(vk) : ''
  if (!props.active || !name) return
  physicalHistory.value = [{ id: ++eventId, name, down, time: new Date() }, ...physicalHistory.value].slice(0, 10)
}))

function timeLabel(time: Date) {
  return time.toLocaleTimeString(props.language === 'ru' ? 'ru-RU' : 'en-GB', { hour12: false }) +
    '.' + time.getMilliseconds().toString().padStart(3, '0')
}

watch(visualPressed, keys => {
  if (!props.active) return
  seen.value = [...new Set([...seen.value, ...keys.filter(vk => byVk.has(vk))])]
})
watch(() => props.active, async visible => {
  session++
  if (visible) { await nextTick(); surface.value?.focus() }
})
onMounted(() => {
  window.addEventListener('keydown', output, true)
  window.addEventListener('keyup', output, true)
})
onUnmounted(() => {
  window.removeEventListener('keydown', output, true)
  window.removeEventListener('keyup', output, true)
})

const arrows: Record<string, string> = { ArrowUp: '↑', ArrowDown: '↓', ArrowLeft: '←', ArrowRight: '→' }

async function output(event: KeyboardEvent) {
  if (!props.active || event.isComposing) return
  const down = event.type === 'keydown'
  const time = new Date()
  if ((event.key === 'CapsLock' || event.code === 'CapsLock') && props.profile.layers.some(item => item.key === 20)) {
    event.preventDefault()
    return
  }
  if (down && event.key !== 'Tab' && event.target instanceof Node && surface.value?.contains(event.target)) event.preventDefault()
  const key = event.key === ' ' ? 'Space' : event.key
  const mods = [event.ctrlKey && 'Ctrl', event.altKey && 'Alt', event.shiftKey && 'Shift', event.metaKey && 'Win']
    .filter(mod => mod && mod !== key && !(mod === 'Ctrl' && key === 'Control') && !(mod === 'Win' && key === 'Meta'))
  const name = [...mods, arrows[key] ? `${arrows[key]} ${key}` : key].join(' + ')
  if (desktop) {
    const currentSession = session
    const code = event.code || (arrows[key] ? key : 'Unidentified')
    try {
      const result = await request('keys.test.output', { code, vk: event.keyCode, down })
      if (!props.active || currentSession !== session || result.ignoreTestOutput || result.error) return
    } catch { return }
  }
  if (down) windowsKey.value = name
  if (!event.repeat) windowsHistory.value = [{ id: ++eventId, name, down, time }, ...windowsHistory.value].slice(0, 10)
}

function reset() {
  session++
  windowsKey.value = ''
  seen.value = []
  physicalHistory.value = []
  windowsHistory.value = []
  surface.value?.focus()
}
</script>

<template>
  <section class="device-card workspace-card" aria-labelledby="key-test-title">
    <div class="device-header">
      <div><h1 id="key-test-title">{{ tr('Тест клавиш', 'Key test') }}</h1><p class="feedback layer-note">{{ desktop ? tr('Нажимай клавиши на Caraxes.', 'Press keys on the Caraxes.') : tr('Открой настольный HUB для физического теста.', 'Open the desktop HUB to test the physical keyboard.') }}</p></div>
      <button @click="reset">{{ tr('Очистить', 'Clear') }}</button>
    </div>
    <div ref="surface" class="test-surface" tabindex="0" :aria-label="tr('Поле теста', 'Key test surface')">
      <KeyLayout mode="test" :label="tr('Физические клавиши', 'Physical keys')" v-bind="layout"
        :pressed="visualPressed" @choose="surface?.focus()" />
    </div>
    <dl class="test-readings">
      <div><dt>{{ tr('Получено в Windows', 'Received in Windows') }}</dt><dd data-test="windows-key">{{ windowsKey || '—' }}</dd></div>
      <div><dt>{{ tr('Активный слой', 'Active layer') }}</dt><dd data-test="layer">{{ stockFn ? keyName(physicalFn) : layer?.name ?? tr('Базовый', 'Base') }}<small v-if="layout.activator">{{ stockFn ? tr('Штатный', 'Stock') : keyName(layout.activator) }}</small></dd></div>
      <div><dt>{{ tr('Проверено', 'Tested') }}</dt><dd>{{ seen.length }} / {{ layoutKeys.length }}</dd></div>
    </dl>
    <div class="test-history">
      <section v-for="history in histories" :key="history.id" :data-test="history.id + '-history'" :aria-labelledby="history.id + '-history-title'">
        <h2 :id="history.id + '-history-title'">{{ history.label }}</h2>
        <ol class="test-events">
          <li v-for="event in history.events" :key="event.id" :class="{ down: event.down }">
            <span>{{ event.name }}</span>
            <span class="test-event-action">{{ event.down ? tr('нажата', 'pressed') : tr('отпущена', 'released') }}</span>
            <time :datetime="event.time.toISOString()">{{ timeLabel(event.time) }}</time>
          </li>
        </ol>
      </section>
    </div>
  </section>
</template>
