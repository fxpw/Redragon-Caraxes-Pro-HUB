<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import KeyLayout from './KeyLayout.vue'
import { layoutKeys } from './keyLayout'
import { desktop, request, type DeviceState } from './bridge'

type Pixels = Record<number, string>
const props = defineProps<{ language: 'ru' | 'en'; device: DeviceState }>()
const emit = defineEmits<{ state: [value: DeviceState] }>()
const tr = (ru: string, en: string) => props.language === 'ru' ? ru : en
const colors = ['#f25a71', '#ff9c56', '#f6d35d', '#65d99e', '#49cdea', '#627cff', '#b487fa', '#ece8f6']
const color = ref(colors[6])
const erasing = ref(false)
const name = ref('')
const opened = ref('')
const previewing = ref(false)
const previewStatus = ref('')

function read<T>(key: string, fallback: T): unknown {
  try { return JSON.parse(localStorage.getItem(key) ?? 'null') ?? fallback }
  catch { return fallback }
}

function pixelsFrom(value: unknown): Pixels {
  const result: Pixels = {}
  if (value && typeof value === 'object') {
    const pixels = value as Record<number, unknown>
    for (const { vk } of layoutKeys) {
      const pixel = pixels[vk]
      if (vk !== 250 && typeof pixel === 'string' && /^#[\da-f]{6}$/i.test(pixel)) result[vk] = pixel
    }
  }
  return result
}

function savedPresets() {
  const value = read('caraxes.rgb.presets.v1', [])
  if (!Array.isArray(value)) return []
  return value.slice(0, 100).filter(row => row && typeof row.name === 'string' && row.name.trim() && row.name.length <= 60)
    .map(row => ({ name: row.name as string, pixels: pixelsFrom(row.pixels) }))
}

const pixels = ref(pixelsFrom(read('caraxes.rgb.draft.v1', {})))
const presets = ref(savedPresets())
const undo = ref<Pixels[]>([])
const redo = ref<Pixels[]>([])
let pointer: number | null = null
let stroke: Pixels | null = null
const nameLength = computed(() => name.value.trim().length)

watch(pixels, value => localStorage.setItem('caraxes.rgb.draft.v1', JSON.stringify(value)))
watch(presets, value => localStorage.setItem('caraxes.rgb.presets.v1', JSON.stringify(value)), { deep: true })

function remember(previous: Pixels) {
  undo.value = [...undo.value.slice(-63), previous]
  redo.value = []
}

function paint(vk: number) {
  const value = erasing.value ? undefined : color.value
  if (pixels.value[vk] === value) return
  if (pointer === null) remember(pixels.value)
  const next = { ...pixels.value }
  if (value) next[vk] = value
  else delete next[vk]
  pixels.value = next
}

function vkAt(element: Element | null, canvas: HTMLElement) {
  const key = element?.closest<HTMLElement>('button[data-vk]:not(:disabled)')
  return key && canvas.contains(key) ? Number(key.dataset.vk) : 0
}

function down(event: PointerEvent) {
  if (event.button !== 0 || pointer !== null) return
  const canvas = event.currentTarget as HTMLElement
  const vk = vkAt(event.target as Element, canvas)
  if (!vk) return
  pointer = event.pointerId
  stroke = pixels.value
  canvas.setPointerCapture(pointer)
  paint(vk)
}

function move(event: PointerEvent) {
  if (event.pointerId !== pointer) return
  const vk = vkAt(document.elementFromPoint(event.clientX, event.clientY), event.currentTarget as HTMLElement)
  if (vk) paint(vk)
}

function end(event: PointerEvent) {
  if (event.pointerId !== pointer) return
  if (stroke && stroke !== pixels.value) remember(stroke)
  stroke = null
  pointer = null
}

function changeHistory(back: boolean) {
  const from = back ? undo : redo
  const to = back ? redo : undo
  const previous = from.value.pop()
  if (!previous) return
  to.value.push(pixels.value)
  pixels.value = previous
}

function clear() {
  if (!Object.keys(pixels.value).length) return
  remember(pixels.value)
  pixels.value = {}
}

function save() {
  const title = name.value.trim()
  if (!title || title.length > 60) return
  const entry = presets.value.find(item => item.name === title)
  if (entry) entry.pixels = { ...pixels.value }
  else if (presets.value.length < 100) presets.value.push({ name: title, pixels: { ...pixels.value } })
  opened.value = title
}

function open() {
  const preset = presets.value.find(item => item.name === opened.value)
  if (!preset) return
  remember(pixels.value)
  pixels.value = { ...preset.pixels }
  name.value = preset.name
}

async function preview() {
  previewing.value = true
  previewStatus.value = ''
  try {
    const reply = await request('canvas.preview', { generation: props.device.generation, colors: { ...pixels.value } })
    emit('state', reply.state)
    previewStatus.value = reply.error ?? tr('Штатная подсветка восстановлена, настройки проверены.', 'Stock lighting was restored and its settings verified.')
  } catch { previewStatus.value = tr('HUB не подтвердил возврат подсветки.', 'The HUB could not confirm lighting restoration.') }
  finally { previewing.value = false }
}
</script>

<template>
  <section class="device-card workspace-card">
    <div class="device-header">
      <h1>{{ tr('RGB-холст', 'RGB canvas') }}</h1>
      <div class="tool-actions">
        <button :disabled="!undo.length" @click="changeHistory(true)">{{ tr('Отменить', 'Undo') }}</button>
        <button :disabled="!redo.length" @click="changeHistory(false)">{{ tr('Повторить', 'Redo') }}</button>
        <button :disabled="!Object.keys(pixels).length" @click="clear">{{ tr('Очистить', 'Clear') }}</button>
      </div>
    </div>
    <div class="palette" :aria-label="tr('Палитра', 'Palette')">
      <button v-for="swatch in colors" :key="swatch" :style="{ backgroundColor: swatch }"
        :aria-label="swatch" :aria-pressed="!erasing && color === swatch" @click="color = swatch; erasing = false"></button>
      <input v-model="color" type="color" :aria-label="tr('Произвольный цвет', 'Custom color')" @input="erasing = false" />
      <button class="eraser" :aria-pressed="erasing" @click="erasing = !erasing">{{ tr('Ластик', 'Eraser') }}</button>
    </div>
    <div class="paint-area" @pointerdown="down" @pointermove="move" @pointerup="end" @pointercancel="end" @lostpointercapture="end">
      <KeyLayout mode="paint" :label="tr('Рисунок по клавишам', 'Key drawing')" :colors="pixels" @choose="paint" />
    </div>
    <div class="canvas-preview">
      <p class="feedback" role="status">{{ previewStatus || (device.canvasAvailable
        ? tr('Рисунок отображается 5 секунд, затем возвращается прежний эффект. Fn1 не участвует в рисунке.', 'The drawing displays for 5 seconds, then the previous effect returns. Fn1 is excluded from the drawing.')
        : tr('Для предпросмотра на клавиатуре подключи её по USB и выбери эффект «Цветение».', 'For keyboard preview, connect over USB and select the Blossoming effect.')) }}</p>
      <button class="primary" :disabled="!desktop || !device.canvasAvailable || previewing || !Object.keys(pixels).length" @click="preview">
        {{ previewing ? tr('Показываю…', 'Previewing…') : tr('Показать на клавиатуре', 'Show on keyboard') }}
      </button>
    </div>
  </section>
  <section class="lighting-card workspace-card presets">
    <h2>{{ tr('Рисунки', 'Drawings') }}</h2>
    <div class="preset-actions">
      <form @submit.prevent="save">
        <input v-model="name" :aria-label="tr('Название рисунка', 'Drawing name')" :placeholder="tr('Название рисунка', 'Drawing name')" maxlength="60" />
        <button class="primary" :disabled="!nameLength || (presets.length >= 100 && !presets.some(item => item.name === name.trim()))">{{ tr('Сохранить', 'Save') }}</button>
      </form>
      <form @submit.prevent="open">
        <select v-model="opened" :aria-label="tr('Сохранённые рисунки', 'Saved drawings')">
          <option value="">{{ tr('Выбрать рисунок', 'Choose drawing') }}</option>
          <option v-for="preset in presets" :key="preset.name" :value="preset.name">{{ preset.name }}</option>
        </select>
        <button :disabled="!opened">{{ tr('Открыть', 'Open') }}</button>
      </form>
    </div>
  </section>
</template>
