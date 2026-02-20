import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      component: () => import('../layouts/AppLayout.vue'),
      children: [
        {
          path: '',
          name: 'dashboard',
          component: () => import('../views/DashboardView.vue'),
        },
        {
          path: 'portfolios',
          name: 'portfolios',
          component: () => import('../views/PortfoliosView.vue'),
        },
        {
          path: 'ai-advisor',
          name: 'ai-advisor',
          component: () => import('../views/AiAdvisorView.vue'),
        },
        {
          path: 'data-quality',
          name: 'data-quality',
          component: () => import('../views/DataQualityView.vue'),
        },
      ],
    },
  ],
})

export default router
