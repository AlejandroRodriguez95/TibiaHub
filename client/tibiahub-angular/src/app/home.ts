import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [FormsModule, RouterLink],
  templateUrl: './home.html',
})
export class Home {
  private readonly router = inject(Router);
  protected readonly name = signal('');
  protected readonly error = signal('');

  protected search(): void {
    const name = this.name().trim();
    if (!name) {
      this.error.set('Enter a character name to begin.');
      return;
    }
    this.error.set('');
    void this.router.navigate(['/characters'], { queryParams: { name } });
  }
}
