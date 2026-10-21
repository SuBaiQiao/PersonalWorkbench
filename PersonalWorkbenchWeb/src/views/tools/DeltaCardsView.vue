<script setup>
import { computed, onMounted, reactive, ref } from 'vue';
import { Modal, message } from 'ant-design-vue';
import { api, getErrorMessage } from '../../api/client.js';

const cards = ref([]);
const loading = ref(false);
const saving = ref(false);
const errorMessage = ref('');
const modalOpen = ref(false);
const editingId = ref(null);

const filters = reactive({ search: '', category: undefined, rarity: undefined, collected: undefined });
const form = reactive(createEmptyForm());

const categoryOptions = ['武器', '装备', '道具', '任务', '其他'];
const rarityOptions = ['普通', '稀有', '史诗', '传说'];

const collectedCount = computed(() => cards.value.filter(card => card.isCollected).length);
const completionRate = computed(() => cards.value.length ? Math.round(collectedCount.value / cards.value.length * 100) : 0);
const modalTitle = computed(() => editingId.value ? '编辑卡牌' : '新增卡牌');

function createEmptyForm() {
    return { name: '', category: undefined, rarity: undefined, description: '', imageUrl: '', notes: '', isCollected: false };
}

function resetForm() {
    Object.assign(form, createEmptyForm());
    editingId.value = null;
}

async function loadCards() {
    loading.value = true;
    errorMessage.value = '';
    try {
        const params = Object.fromEntries(Object.entries(filters).filter(([, value]) => value !== undefined && value !== ''));
        const response = await api.get('/api/delta-cards', { params });
        cards.value = response.data;
    } catch (error) {
        errorMessage.value = await getErrorMessage(error, '卡牌列表加载失败。');
    } finally {
        loading.value = false;
    }
}

function openCreate() {
    resetForm();
    modalOpen.value = true;
}

function openEdit(card) {
    editingId.value = card.id;
    Object.assign(form, {
        name: card.name,
        category: card.category,
        rarity: card.rarity,
        description: card.description || '',
        imageUrl: card.imageUrl || '',
        notes: card.notes || '',
        isCollected: card.isCollected
    });
    modalOpen.value = true;
}

async function saveCard() {
    if (!form.name || !form.category || !form.rarity) {
        message.warning('请填写卡牌名称、分类和稀有度。');
        return;
    }

    saving.value = true;
    try {
        const payload = { ...form };
        if (editingId.value) {
            await api.put(`/api/delta-cards/${editingId.value}`, payload);
            message.success('卡牌已更新。');
        } else {
            await api.post('/api/delta-cards', payload);
            message.success('卡牌已添加。');
        }
        modalOpen.value = false;
        await loadCards();
    } catch (error) {
        message.error(await getErrorMessage(error, '卡牌保存失败。'));
    } finally {
        saving.value = false;
    }
}

async function toggleCollected(card) {
    try {
        await api.patch(`/api/delta-cards/${card.id}/collection`, { isCollected: !card.isCollected });
        card.isCollected = !card.isCollected;
        message.success(card.isCollected ? '已标记为已收集。' : '已取消收集标记。');
    } catch (error) {
        message.error(await getErrorMessage(error, '收集状态更新失败。'));
    }
}

function confirmDelete(card) {
    Modal.confirm({
        title: '删除这张卡牌？',
        content: `“${card.name}”删除后无法恢复。`,
        okText: '删除',
        cancelText: '取消',
        okButtonProps: { danger: true },
        async onOk() {
            try {
                await api.delete(`/api/delta-cards/${card.id}`);
                message.success('卡牌已删除。');
                await loadCards();
            } catch (error) {
                message.error(await getErrorMessage(error, '卡牌删除失败。'));
            }
        }
    });
}

function clearFilters() {
    Object.assign(filters, { search: '', category: undefined, rarity: undefined, collected: undefined });
    loadCards();
}

onMounted(loadCards);
</script>

<template>
    <section class="delta-page" aria-labelledby="delta-title">
        <header class="delta-header">
            <div>
                <p class="eyebrow">DELTA FORCE COLLECTION</p>
                <h1 id="delta-title">三角洲卡牌收集</h1>
                <p class="delta-description">记录卡牌资料和收集进度，数据保存在本地 SQLite 数据库中。</p>
            </div>
            <a-button type="primary" size="large" @click="openCreate">新增卡牌</a-button>
        </header>

        <section class="delta-summary" aria-label="收集进度">
            <div><span>当前结果</span><strong>{{ cards.length }}</strong><small>张卡牌</small></div>
            <div><span>已收集</span><strong>{{ collectedCount }}</strong><small>张</small></div>
            <div><span>完成度</span><strong>{{ completionRate }}%</strong><small>按当前筛选结果</small></div>
        </section>

        <section class="delta-toolbar" aria-label="卡牌筛选">
            <a-input-search v-model:value="filters.search" placeholder="搜索名称或描述" allow-clear @search="loadCards" @press-enter="loadCards" />
            <a-select v-model:value="filters.category" placeholder="全部分类" allow-clear @change="loadCards">
                <a-select-option v-for="category in categoryOptions" :key="category" :value="category">{{ category }}</a-select-option>
            </a-select>
            <a-select v-model:value="filters.rarity" placeholder="全部稀有度" allow-clear @change="loadCards">
                <a-select-option v-for="rarity in rarityOptions" :key="rarity" :value="rarity">{{ rarity }}</a-select-option>
            </a-select>
            <a-select v-model:value="filters.collected" placeholder="全部状态" allow-clear @change="loadCards">
                <a-select-option :value="true">已收集</a-select-option>
                <a-select-option :value="false">未收集</a-select-option>
            </a-select>
            <a-button @click="clearFilters">重置</a-button>
        </section>

        <a-alert v-if="errorMessage" :message="errorMessage" type="error" show-icon />
        <div v-if="loading" class="delta-state" role="status">正在读取卡牌数据…</div>
        <a-empty v-else-if="!cards.length" description="暂无符合条件的卡牌">
            <template #extra><a-button type="primary" @click="openCreate">添加第一张卡牌</a-button></template>
        </a-empty>
        <div v-else class="delta-card-grid">
            <article v-for="card in cards" :key="card.id" class="delta-card" :class="{ collected: card.isCollected }">
                <div class="delta-card-image">
                    <img v-if="card.imageUrl" :src="card.imageUrl" :alt="card.name" loading="lazy" />
                    <span v-else aria-hidden="true">{{ card.name.slice(0, 1) }}</span>
                    <span class="delta-rarity">{{ card.rarity }}</span>
                </div>
                <div class="delta-card-body">
                    <div class="delta-card-heading"><h2>{{ card.name }}</h2><span>{{ card.category }}</span></div>
                    <p v-if="card.description">{{ card.description }}</p>
                    <p v-else class="muted">暂无描述</p>
                    <p v-if="card.notes" class="delta-notes">备注：{{ card.notes }}</p>
                    <div class="delta-card-actions">
                        <a-button :type="card.isCollected ? 'primary' : 'default'" @click="toggleCollected(card)">{{ card.isCollected ? '已收集' : '标记收集' }}</a-button>
                        <a-button type="text" @click="openEdit(card)">编辑</a-button>
                        <a-button type="text" danger @click="confirmDelete(card)">删除</a-button>
                    </div>
                </div>
            </article>
        </div>
    </section>

    <a-modal v-model:open="modalOpen" :title="modalTitle" :confirm-loading="saving" ok-text="保存" cancel-text="取消" @ok="saveCard">
        <a-form layout="vertical" class="delta-form">
            <a-form-item label="卡牌名称" required><a-input v-model:value="form.name" maxlength="120" placeholder="例如：长弓溪谷通行证" /></a-form-item>
            <div class="delta-form-row">
                <a-form-item label="分类" required><a-select v-model:value="form.category" placeholder="选择分类"><a-select-option v-for="category in categoryOptions" :key="category" :value="category">{{ category }}</a-select-option></a-select></a-form-item>
                <a-form-item label="稀有度" required><a-select v-model:value="form.rarity" placeholder="选择稀有度"><a-select-option v-for="rarity in rarityOptions" :key="rarity" :value="rarity">{{ rarity }}</a-select-option></a-select></a-form-item>
            </div>
            <a-form-item label="图片地址"><a-input v-model:value="form.imageUrl" maxlength="500" placeholder="可选的图片 URL" /></a-form-item>
            <a-form-item label="描述"><a-textarea v-model:value="form.description" :rows="3" maxlength="1000" show-count /></a-form-item>
            <a-form-item label="备注"><a-textarea v-model:value="form.notes" :rows="2" maxlength="1000" show-count /></a-form-item>
            <a-checkbox v-model:checked="form.isCollected">已收集</a-checkbox>
        </a-form>
    </a-modal>
</template>

<style scoped>
.delta-page { max-width: 1180px; margin: 0 auto; padding: 0 0 32px; }
.delta-header { display: flex; justify-content: space-between; align-items: flex-end; gap: 24px; margin-bottom: 28px; }
.delta-header h1 { margin: 4px 0 8px; font-size: clamp(28px, 4vw, 44px); line-height: 1.1; }
.delta-description { max-width: 620px; margin: 0; color: var(--text-secondary); }
.delta-summary { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; margin-bottom: 20px; }
.delta-summary > div { padding: 22px; border: 1px solid var(--glass-border); border-radius: 22px; background: var(--surface); box-shadow: var(--shadow-glass); }
.delta-summary span, .delta-summary small { display: block; color: var(--text-secondary); }
.delta-summary strong { display: inline-block; margin: 8px 8px 0 0; font-size: 28px; color: var(--text-primary); }
.delta-toolbar { display: grid; grid-template-columns: minmax(200px, 1.8fr) repeat(3, minmax(130px, 1fr)) auto; gap: 10px; margin-bottom: 24px; }
.delta-state { padding: 56px 0; color: var(--text-secondary); text-align: center; }
.delta-card-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px; }
.delta-card { overflow: hidden; border: 1px solid var(--glass-border); border-radius: 24px; background: var(--surface); box-shadow: var(--shadow-glass); transition: border-color .2s, transform .2s; }
.delta-card:hover { border-color: var(--accent); transform: translateY(-2px); }
.delta-card.collected { border-color: var(--accent); }
.delta-card-image { position: relative; display: grid; aspect-ratio: 16 / 8; place-items: center; background: linear-gradient(135deg, #1b2838, #394d61); color: #d7e4ef; font-size: 40px; font-weight: 700; }
.delta-card-image img { width: 100%; height: 100%; object-fit: cover; }
.delta-rarity { position: absolute; right: 12px; bottom: 12px; padding: 5px 10px; border: 1px solid rgba(255, 255, 255, .24); border-radius: 10px; background: rgba(12, 19, 27, .72); color: #fff; font-size: 12px; backdrop-filter: blur(12px); }
.delta-card-body { padding: 16px; }
.delta-card-heading { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.delta-card-heading h2 { margin: 0; font-size: 18px; }
.delta-card-heading span { color: var(--text-secondary); font-size: 12px; white-space: nowrap; }
.delta-card-body p { min-height: 42px; margin: 12px 0 0; color: var(--text-secondary); font-size: 14px; line-height: 1.5; }
.delta-card-body .muted { color: var(--text-tertiary); }
.delta-card-body .delta-notes { min-height: 0; color: var(--text-primary); font-size: 13px; }
.delta-card-actions { display: flex; align-items: center; gap: 4px; margin-top: 16px; }
.delta-card-actions .ant-btn:first-child { margin-right: auto; }
.delta-form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
@media (max-width: 760px) {
    .delta-page { padding: 0 0 24px; }
    .delta-header { align-items: flex-start; flex-direction: column; }
    .delta-header .ant-btn { width: 100%; }
    .delta-summary { grid-template-columns: 1fr 1fr; }
    .delta-summary > div:last-child { grid-column: 1 / -1; }
    .delta-toolbar { grid-template-columns: 1fr 1fr; }
    .delta-toolbar .ant-input-search { grid-column: 1 / -1; }
    .delta-toolbar .ant-btn { grid-column: 1 / -1; }
}
</style>
