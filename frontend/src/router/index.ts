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
          path: 'portfolios/new', 
          name: 'portfolio-create', 
          component: () => import('../views/PortfolioCreateView.vue')
        },
        { 
          path: 'portfolios/:id', 
          name: 'portfolio-detail', 
          component: () => import('../views/PortfoliosView.vue')
        },
        {
          path: 'data-quality',
          name: 'data-quality',
          component: () => import('../views/DataQualityView.vue'),
        },
        {
          path: 'portfolios/:id/import',
          name: 'csv-import',
          component: () => import('../views/CsvImportView.vue'),
        },
      ],
    },
  ],
})

export default router
