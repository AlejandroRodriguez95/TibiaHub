import { Component, HostListener, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly toolsOpen = signal(false);
  protected readonly toggleTools = (open: boolean) => !open;

  @HostListener('document:click', ['$event'])
  protected closeToolsOutside(event: MouseEvent): void {
    if (!(event.target instanceof Element) || !event.target.closest('.tools-dropdown')) this.toolsOpen.set(false);
  }
}
