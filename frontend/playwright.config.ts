import { defineConfig } from '@playwright/test';
export default defineConfig({testDir:'./e2e',timeout:60000,workers:1,use:{baseURL:process.env.BASE_URL||'http://localhost:8088',viewport:{width:1366,height:900},channel:process.env.PW_CHANNEL||'chrome',trace:'retain-on-failure'},reporter:'list'});
