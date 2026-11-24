<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { actionLabel, targetExtra, targetMain, targetMedia, targetNavigation, targetNumpad, type Assignment } from './keyLayout'

const props = defineProps<{ source: string; assignment?: Assignment; language: 'ru' | 'en' }>()
const emit = defineEmits<{ apply: [assignment: Assignment | null]; dismiss: [] }>()
const tr = (ru: string, en: string) => props.language === 'ru' ? ru : en
const dialog = ref<HTMLDialogElement>()
const target = ref(props.assignment?.key ?? 0)
const preserve = ref(props.assignment?.preserve ?? true)

onMounted(() => dialog.value?.showModal())
onBeforeUnmount(() => dialog.value?.close())
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="assignment-dialog workspace-card" aria-labelledby="assignment-title"
      @cancel.prevent="emit('dismiss')" @click.self="emit('dismiss')">
      <div class="assignment-content">
        <header class="assignment-header">
          <h2 id="assignment-title" tabindex="-1" autofocus>{{ tr('Назначение для', 'Assignment for') }} {{ source }}</h2>
          <p class="feedback">{{ target ? actionLabel(target) : tr('По умолчанию', 'Default') }}</p>
        </header>
        <div class="assignment-body">
          <div class="target-keyboard" role="group" :aria-label="tr('Виртуальная клавиатура', 'Virtual keyboard')">
            <div class="target-strip" role="group" :aria-label="tr('Мультимедиа', 'Media')">
              <button v-for="item in targetMedia" :key="item.vk" :aria-pressed="target === item.vk" @click="target = item.vk">{{ item.label }}</button>
            </div>
            <div class="target-scroll">
              <div class="target-blocks">
                <div class="target-main">
                  <div v-for="(row, rowIndex) in targetMain" :key="rowIndex" class="target-row">
                    <button v-for="item in row" :key="item.vk" :aria-pressed="target === item.vk" :aria-label="actionLabel(item.vk)"
                      :style="{ flexGrow: item.width ?? 1 }" @click="target = item.vk">{{ item.label }}</button>
                  </div>
                </div>
                <div class="target-navigation" role="group" :aria-label="tr('Навигация', 'Navigation')">
                  <button v-for="item in targetNavigation.flat()" :key="item.vk" :data-vk="item.vk"
                    :aria-pressed="target === item.vk" @click="target = item.vk">{{ item.label }}</button>
                </div>
                <div class="target-numpad" role="group" :aria-label="tr('Цифровой блок', 'Numpad')">
                  <button v-for="item in targetNumpad.flat()" :key="item.vk" :data-vk="item.vk"
                    :aria-pressed="target === item.vk" :aria-label="actionLabel(item.vk)" @click="target = item.vk">{{ item.label }}</button>
                </div>
              </div>
            </div>
            <div class="target-strip">
              <button v-for="item in targetExtra" :key="item.vk" :aria-pressed="target === item.vk" @click="target = item.vk">{{ item.label }}</button>
              <button :aria-pressed="target === 0" @click="target = 0">{{ tr('По умолчанию', 'Default') }}</button>
            </div>
          </div>
          <div v-if="target !== 256" class="assignment-modifiers">
            <label><input v-model="preserve" type="checkbox" aria-describedby="assignment-modifiers-help" />{{ tr('Передавать зажатые Ctrl, Alt, Shift и Win', 'Pass through held Ctrl, Alt, Shift and Win') }}</label>
            <p id="assignment-modifiers-help" class="feedback">{{ tr('Например, для назначения ↑: с галочкой зажатый Shift даст Shift + ↑, без галочки — только ↑.', 'For an ↑ assignment, holding Shift gives Shift + ↑ when checked and only ↑ when unchecked.') }}</p>
          </div>
        </div>
        <footer class="assignment-footer actions">
          <button @click="emit('dismiss')">{{ tr('Отмена', 'Cancel') }}</button>
          <button class="primary" @click="emit('apply', target ? { key: target, preserve } : null)">{{ tr('Готово', 'Done') }}</button>
        </footer>
      </div>
    </dialog>
  </Teleport>
</template>
