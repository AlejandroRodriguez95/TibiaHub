import { Routes } from '@angular/router';
import { Account } from './account';
import { CharacterLookup } from './character-lookup';
import { Admin } from './admin';
import { Home } from './home';
import { Tools } from './tools';

export const routes: Routes = [
  { path: 'admin', component: Admin, title: 'Admin panel | TibiaHub' },
  { path: '', component: Home, title: 'Your next adventure | TibiaHub' },
  { path: 'characters', component: CharacterLookup, title: 'Character lookup | TibiaHub' },
  { path: 'tools', component: Tools, title: 'NPC selling | TibiaHub' },
  { path: 'login', component: Account, data: { mode: 'login' }, title: 'Log in | TibiaHub' },
  { path: 'register', component: Account, data: { mode: 'register' }, title: 'Register | TibiaHub' },
  { path: 'account', component: Account, data: { mode: 'account' }, title: 'My account | TibiaHub' },
  { path: '**', redirectTo: '' },
];
