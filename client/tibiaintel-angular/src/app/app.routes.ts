import { Routes } from '@angular/router';
import { Account } from './account';
import { CharacterLookup } from './character-lookup';
import { Admin } from './admin';
import { Home } from './home';
import { Tools } from './tools';
import { CharacterScan } from './character-scan';

export const routes: Routes = [
  { path: 'admin', component: Admin, title: 'Admin panel | TibiaIntel' },
  { path: '', component: Home, title: 'Your next adventure | TibiaIntel' },
  { path: 'characters', component: CharacterLookup, title: 'Character lookup | TibiaIntel' },
  { path: 'tools/character-scan', component: CharacterScan, title: 'Character scan | TibiaIntel' },
  { path: 'tools/npc-selling', component: Tools, title: 'NPC selling | TibiaIntel' },
  { path: 'scans', redirectTo: 'tools/character-scan', pathMatch: 'full' },
  { path: 'tools', redirectTo: 'tools/npc-selling', pathMatch: 'full' },
  { path: 'login', component: Account, data: { mode: 'login' }, title: 'Log in | TibiaIntel' },
  { path: 'register', component: Account, data: { mode: 'register' }, title: 'Register | TibiaIntel' },
  { path: 'account', component: Account, data: { mode: 'account' }, title: 'My account | TibiaIntel' },
  { path: '**', redirectTo: '' },
];
