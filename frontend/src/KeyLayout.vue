<script setup lang="ts">
import { actionLabel, keyName, modifiers, rows, type Bindings } from './keyLayout'

const props = defineProps<{
  label: string
  mode: 'layer' | 'paint' | 'test'
  bindings?: Bindings
  inherited?: Bindings
  factory?: Record<number, string>
  pressed?: number[]
  activator?: number
  layerKeys?: Record<number, string>
  selected?: number
  colors?: Record<number, string>
}>()
defineEmits<{ choose: [vk: number] }>()

function caption(vk: number) {
  if (props.layerKeys?.[vk] && !props.factory) return props.layerKeys[vk]
  if (!props.bindings?.[vk] && props.factory?.[vk]) return props.factory[vk]
  const action = props.bindings?.[vk] ?? props.inherited?.[vk]
  if (!action) return ''
  if (action.key === 256) return props.pressed?.some(code => code >= 160 && code <= 163) ? '~' : '`'
  return actionLabel(action.key)
}
</script>

<template>
  <div class="keyboard key-editor" :class="mode" role="group" :aria-label="label">
    <div v-for="(row, index) in rows" :key="index" class="key-row">
      <button v-for="item in row" :key="item.vk" class="key" type="button"
        :data-vk="item.vk" :tabindex="mode === 'test' ? -1 : undefined"
        :disabled="mode === 'paint' && item.vk === 250"
        :aria-label="caption(item.vk) ? `${keyName(item.vk)} → ${caption(item.vk)}` : keyName(item.vk)" :title="keyName(item.vk)"
        :class="{ modifier: modifiers.has(item.vk) || layerKeys?.[item.vk], pressed: pressed?.includes(item.vk), activator: item.vk === activator,
          selected: item.vk === selected, inherited: mode !== 'paint' && !modifiers.has(item.vk) && !bindings?.[item.vk] && !factory?.[item.vk] }"
        :style="{ flexGrow: item.width ?? 1, '--pixel': colors?.[item.vk] ?? 'transparent' }"
        @click="$emit('choose', item.vk)">
        <strong v-if="caption(item.vk)">{{ caption(item.vk) }}</strong><span v-else>{{ item.label }}</span>
      </button>
    </div>
  </div>
</template>
