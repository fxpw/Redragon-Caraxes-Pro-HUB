<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { desktop, onCapsLock, request } from './bridge'
import { hardwareLayerKey, heldLayer, keyName, layerLayout, layoutKeys, modifiers, runtimeProfile, type Assignment, type Layer, type LayerProfile } from './keyLayout'
import KeyLayout from './KeyLayout.vue'
import KeyPicker from './KeyPicker.vue'

const props = defineProps<{ language: 'en' | 'ru'; profile: LayerProfile; pressed: number[] }>()
const windowsLayersSetting = 'caraxes.windows-layers'
const tr = (ru: string, en: string) => props.language === 'ru' ? ru : en
const current = ref(props.profile.layers[0]?.id ?? '')
const selected = ref(0)
const pickingTrigger = ref(false)
const draft = ref<Layer | null>(null)
const deleting = ref(false)
const active = ref(false)
const pending = ref(false)
const issue = ref('')
const capsLock = ref<boolean | null>(null)
const capsPending = ref(false)
const capsIssue = ref(false)
onUnmounted(onCapsLock(enabled => { capsLock.value = enabled }))
const physicalFn = computed(() => hardwareLayerKey(props.pressed))
const held = computed(() => heldLayer(props.profile, props.pressed))
const edited = computed(() => props.profile.layers.find(layer => layer.id === current.value))
const draftProfile = computed<LayerProfile>(() => {
  const layer = draft.value
  if (!layer) return props.profile
  const layers = props.profile.layers.map(item => item.id === layer.id ? layer : item)
  if (!edited.value) layers.push(layer)
  return { base: props.profile.base, layers }
})
const viewed = computed(() => held.value ?? (physicalFn.value ? undefined : draft.value ?? edited.value))
const layout = computed(() => layerLayout(draftProfile.value, viewed.value, props.language, physicalFn.value))
const bindings = computed(() => draft.value?.bindings ?? edited.value?.bindings ?? props.profile.base)
const binding = computed(() => bindings.value[selected.value])
const applied = ref('')
const changed = computed(() => applied.value !== JSON.stringify(runtimeProfile(props.profile)))
const isLayerHeld = computed(() => Boolean(held.value || physicalFn.value))

function selectLayer(id: string) {
  current.value = id
  selected.value = 0
  draft.value = null
  deleting.value = false
  pickingTrigger.value = false
  issue.value = ''
}

function addLayer() {
  const id = crypto.randomUUID()
  const prefix = tr('Слой ', 'Layer ')
  let number = 1
  while (props.profile.layers.some(layer => layer.name === prefix + number)) number++
  selectLayer(id)
  draft.value = { id, name: prefix + number, key: 0, bindings: {} }
  pickingTrigger.value = true
}

function editLayer(layer: Layer) {
  deleting.value = false
  issue.value = ''
  draft.value = {
    ...layer,
    bindings: Object.fromEntries(Object.entries(layer.bindings).map(([key, assignment]) => [key, { ...assignment }])),
  }
}

function saveLayer() {
  const layer = draft.value
  if (!layer || !layer.name.trim()) return
  const saved = { ...layer, name: layer.name.trim() }
  const index = props.profile.layers.findIndex(item => item.id === layer.id)
  if (index === -1) props.profile.layers.push(saved)
  else props.profile.layers.splice(index, 1, saved)
  selectLayer(layer.id)
}

function deleteLayer() {
  const index = props.profile.layers.findIndex(item => item.id === current.value)
  if (!deleting.value || index === -1) return
  props.profile.layers.splice(index, 1)
  selectLayer(props.profile.layers[Math.min(index, props.profile.layers.length - 1)]?.id ?? '')
}

function choose(vk: number) {
  if (pickingTrigger.value && draft.value) {
    if (vk === 250 || vk === 251) {
      issue.value = tr('Физические Fn1 и Fn2 управляются клавиатурой. Для своего слоя выбери обычную клавишу.', 'Physical Fn keys are handled by the keyboard. Choose a standard key for this layer.')
      return
    }
    if (props.profile.layers.some(layer => layer.id !== draft.value?.id && layer.key === vk)) {
      issue.value = tr('Эта клавиша уже включает другой слой.', 'This key already activates another layer.')
      return
    }
    draft.value.key = vk
    pickingTrigger.value = false
    issue.value = ''
    selected.value = 0
    return
  }
  const target = draftProfile.value.layers.find(layer => layer.key === vk)
  if (target) {
    if (!draft.value) selectLayer(target.id)
    return
  }
  if (!modifiers.has(vk) && !isLayerHeld.value && !deleting.value) selected.value = vk
}

function assign(assignment: Assignment | null) {
  const source = selected.value
  const previous = binding.value
  selected.value = 0
  if (!source || (assignment?.key === previous?.key && assignment?.preserve === previous?.preserve)) return
  if (!draft.value && edited.value) editLayer(edited.value)
  if (assignment) bindings.value[source] = assignment
  else delete bindings.value[source]
}

watch(isLayerHeld, isHeld => { if (!isHeld && !selected.value && !draft.value && !deleting.value) selectLayer('') })
onMounted(() => {
  if (desktop && localStorage.getItem(windowsLayersSetting) !== 'off') void toggle(true, false)
})

async function toggle(enable: boolean, remember = true) {
  pending.value = true
  issue.value = ''
  const map = JSON.stringify(runtimeProfile(props.profile))
  try {
    const reply = await request(enable ? 'layers.start' : 'layers.stop', enable ? JSON.parse(map) : {})
    active.value = Boolean(reply.layersActive)
    if (reply.error) issue.value = reply.error
    else {
      applied.value = enable ? map : ''
      if (remember) localStorage.setItem(windowsLayersSetting, enable ? 'on' : 'off')
    }
  } catch { issue.value = tr('HUB не ответил.', 'The HUB did not respond.') }
  finally { pending.value = false }
}

async function toggleCapsLock() {
  if (!desktop || capsLock.value === null || capsPending.value) return
  capsPending.value = true
  capsIssue.value = false
  try {
    const reply = await request('caps.set', { enabled: !capsLock.value })
    capsIssue.value = Boolean(reply.error)
  } catch { capsIssue.value = true }
  finally { capsPending.value = false }
}
</script>

<template>
  <section class="device-card workspace-card">
    <div class="device-header">
      <h1>{{ tr('Слои клавиш', 'Key layers') }}</h1>
      <button :disabled="!!draft || deleting || profile.layers.length >= layoutKeys.length" @click="addLayer">{{ tr('Создать слой', 'Create layer') }}</button>
    </div>
    <div class="layer-tabs" :aria-label="tr('Слой', 'Layer')">
      <button :aria-pressed="!viewed" :disabled="!!draft || deleting" @click="selectLayer('')">{{ tr('Базовый', 'Base') }}</button>
      <button v-for="layer in draftProfile.layers" :key="layer.id" :aria-pressed="viewed?.id === layer.id" :disabled="!!draft || deleting" @click="selectLayer(layer.id)">{{ layer.name }}</button>
    </div>
    <div v-if="edited && !draft && !deleting" class="layer-controls">
      <p class="feedback">{{ tr('Модификатор', 'Modifier') }} · {{ keyName(edited.key) || tr('без клавиши', 'unassigned') }}</p>
      <button :disabled="isLayerHeld" @click="editLayer(edited)">{{ tr('Редактировать слой', 'Edit layer') }}</button>
      <button class="layer-delete" :disabled="isLayerHeld" @click="deleting = true; selected = 0">{{ tr('Удалить слой', 'Delete layer') }}</button>
    </div>
    <div v-if="edited && deleting" class="layer-controls" role="group" :aria-label="tr('Подтверждение удаления', 'Confirm deletion')">
      <p class="feedback">{{ tr('Удалить слой «', 'Delete “') + edited.name + tr('» и его назначения?', '” and its assignments?') }}</p>
      <button class="layer-delete" :disabled="isLayerHeld" @click="deleteLayer">{{ tr('Подтвердить удаление', 'Confirm delete') }}</button>
      <button @click="deleting = false">{{ tr('Отмена', 'Cancel') }}</button>
    </div>
    <div v-if="draft" class="layer-controls">
      <label>{{ tr('Имя слоя', 'Layer name') }} <input v-model="draft.name" maxlength="48" @keydown.enter.prevent="($event.target as HTMLInputElement).blur()" /></label>
      <button :aria-pressed="pickingTrigger" :disabled="isLayerHeld" @click="pickingTrigger = !pickingTrigger; selected = 0">
        {{ tr('Модификатор', 'Modifier') }} · {{ keyName(draft.key) || tr('выбрать', 'choose') }}
      </button>
      <button v-if="draft.key" :disabled="isLayerHeld" @click="draft.key = 0; pickingTrigger = false">{{ tr('Снять клавишу слоя', 'Clear layer key') }}</button>
      <button class="primary" :disabled="!draft.name.trim() || isLayerHeld" @click="saveLayer">{{ tr('Сохранить слой', 'Save layer') }}</button>
      <button @click="selectLayer(edited?.id ?? '')">{{ tr('Отмена', 'Cancel') }}</button>
    </div>
    <p v-if="pickingTrigger" class="feedback layer-note">{{ tr('Нажми нужную клавишу на схеме.', 'Click the key you want on the layout.') }}</p>
    <KeyLayout mode="layer" :label="tr('Назначения клавиш', 'Key assignments')" v-bind="layout" :pressed="pressed"
      :selected="selected" @choose="choose" />
    <p v-if="!pickingTrigger && !layout.factory" class="feedback layer-note">{{ tr('Выбери клавишу на схеме, чтобы изменить назначение.', 'Select a key on the layout to change its assignment.') }}</p>
    <KeyPicker v-if="selected" :source="keyName(selected)" :assignment="binding" :language="language"
      @dismiss="selected = 0" @apply="assign" />
    <p class="feedback layer-note" v-if="layout.factory">{{ tr('Физические Fn1 и Fn2 выполняют команды клавиатуры. Для своих сочетаний в Windows выбери обычную клавишу слоя.', 'Physical Fn keys run keyboard shortcuts. Choose a standard layer key for custom Windows assignments.') }}</p>
    <p v-if="issue" class="feedback problem" role="status">{{ issue === 'capsConflict' ? tr('Сначала закрой запущенный caps.exe.', 'Close the running caps.exe first.') : issue }}</p>
  </section>
  <section class="lighting-card workspace-card caps-options" :aria-label="tr('Слои Windows', 'Windows layers')">
    <h2>{{ tr('Слои Windows', 'Windows layers') }}</h2>
    <p class="feedback">{{ tr('Этот режим действует на всех клавиатурах Windows, пока открыт HUB.', 'This mode applies to all Windows keyboards while the HUB is open.') }}</p>
    <div class="actions">
      <button :aria-pressed="capsLock ?? undefined" :disabled="!desktop || capsLock === null || capsPending"
        @click="toggleCapsLock">Caps Lock · {{ capsLock === null ? '—' : capsLock ? tr('Заглавные', 'Uppercase') : tr('Строчные', 'Lowercase') }}</button>
      <button v-if="active && changed" class="primary" :disabled="pending || !!draft || deleting" @click="toggle(true)">{{ tr('Применить назначения', 'Apply assignments') }}</button>
      <button class="primary" :disabled="!desktop || pending" @click="toggle(!active)">{{ active ? tr('Выключить слои в Windows', 'Disable layers in Windows') : tr('Включить слои в Windows', 'Enable layers in Windows') }}</button>
    </div>
    <p v-if="capsIssue" class="feedback problem" role="status">{{ tr('Не удалось подтвердить переключение Caps Lock.', 'Could not verify the Caps Lock change.') }}</p>
  </section>
</template>
