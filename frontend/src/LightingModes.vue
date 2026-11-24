<script setup lang="ts">
import { computed, ref } from 'vue'
import { desktop, request, type DeviceState } from './bridge'

const props = defineProps<{ language: 'ru' | 'en'; device: DeviceState }>()
const emit = defineEmits<{ state: [value: DeviceState] }>()
const tr = (ru: string, en: string) => props.language === 'ru' ? ru : en
const modes = computed(() => [
  { id: 'wave', title: tr('Радужная волна', 'Rainbow wave'), art: 'wave' },
  { id: 'spectrum', title: tr('Эквалайзер', 'Equalizer'), art: 'spectrum' },
  { id: 'aurora', title: tr('Сияние', 'Aurora'), art: 'aurora' },
  { id: 'comet', title: tr('Комета', 'Comet'), art: 'comet' },
  { id: 'breathe', title: tr('Дыхание', 'Breathing'), art: 'breathe' },
])
const busy = ref(false)
const error = ref('')
async function select(mode?: string) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    const reply = await request(mode ? 'effect.start' : 'effect.stop', mode ? { mode, generation: props.device.generation } : {})
    emit('state', reply.state)
    if (reply.error) error.value = reply.error
  } catch { error.value = tr('HUB не ответил.', 'The HUB did not respond.') }
  finally { busy.value = false }
}
</script>

<template>
  <section class="lighting-card workspace-card" aria-labelledby="effects-title">
    <div class="section-head">
      <h2 id="effects-title">{{ tr('Эффекты', 'Effects') }}</h2>
      <button :disabled="busy || !device.effect" @click="select()">{{ tr('Вернуть штатную подсветку', 'Restore stock lighting') }}</button>
    </div>
    <div class="effect-grid">
      <button v-for="mode in modes" :key="mode.id" class="effect-choice" :disabled="!desktop || !device.canvasAvailable || busy"
        :aria-pressed="device.effect === mode.id" @click="select(mode.id)">
        <span class="effect-art" :class="mode.art" aria-hidden="true"></span>
        <span>{{ mode.title }}</span>
      </button>
    </div>
    <p v-if="error" class="feedback problem layer-note" role="status">{{ error }}</p>
    <p v-else-if="device.effect === 'spectrum'" class="feedback layer-note" role="status">{{ tr('Эквалайзер реагирует на звук текущего выхода Windows.', 'The equalizer follows the current Windows audio output.') }}</p>
    <p v-else-if="device.receiverPresent && device.connection !== 'usb'" class="feedback layer-note">{{ tr('Для эффектов HUB подключи кабель.', 'Connect the cable for HUB effects.') }}</p>
    <p v-else-if="!device.canvasAvailable" class="feedback layer-note">{{ tr('Для эффектов подключи Caraxes по USB и выбери штатное «Цветение».', 'Connect the Caraxes over USB and select the stock Blossoming effect.') }}</p>
  </section>
</template>
