<template>
    <div class="flex flex-col space-y-3">
        <div
            v-if="manifest.description"
            class="text-secondary-content/80 text-xs"
            v-html="manifest.description" />
        <div
            v-if="manifest.settings.length === 0 && !manifest.description"
            class="text-sm opacity-60">
            This provider has no configurable settings.
        </div>

        <PluginField
            v-for="field in manifest.settings"
            :key="field.key"
            :field="field"
            @save="emit('save')" />

        <div v-if="testSupported" class="flex items-center space-x-3">
            <ButtonComponent variant="ghost" size="xs" :loading="testing" @click="testConnection">
                Test connection
            </ButtonComponent>
            <span
                v-if="testResult"
                class="text-xs"
                :class="testResult.success ? 'text-green-500' : 'text-red-500'">
                {{ testResult.message || (testResult.success ? 'Connection OK' : 'Connection failed') }}
            </span>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { IPluginConnectionResult, IPluginManifest } from '@/ts'
import services from '@/services'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import PluginField from '@/components/features/settings/PluginField.vue'

const props = defineProps<{
    manifest: IPluginManifest
}>()

const emit = defineEmits(['save'])

const testSupported = ref(true)
const testing = ref(false)
const testResult = ref<IPluginConnectionResult | null>(null)

watch(
    () => props.manifest.provider,
    () => {
        testSupported.value = true
        testResult.value = null
    }
)

async function testConnection() {
    testing.value = true
    testResult.value = null
    try {
        testResult.value = await services.plugin.testConnection(props.manifest.provider)
    } catch (error) {
        const status = (error as { status?: number } | undefined)?.status
        if (status === 404) {
            // Provider does not implement IConnectionTestable; hide the action.
            testSupported.value = false
        } else {
            testResult.value = { success: false, message: 'Connection test request failed.' }
        }
    } finally {
        testing.value = false
    }
}
</script>
