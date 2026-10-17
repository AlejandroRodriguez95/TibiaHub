import { Routes } from '@angular/router';
import { Account } from './account';
import { CharacterLookup } from './character-lookup';

export const routes: Routes = [
  { path: '', component: CharacterLookup, title: 'Character lookup | TibiaHub' },
  { path: 'login', component: Account, data: { mode: 'login' }, title: 'Log in | TibiaHub' },
  { path: 'register', component: Account, data: { mode: 'register' }, title: 'Register | TibiaHub' },
  { path: 'account', component: Account, data: { mode: 'account' }, title: 'My account | TibiaHub' },
  { path: '**', redirectTo: '' },
];
