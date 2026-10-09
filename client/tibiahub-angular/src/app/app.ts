import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';

interface TibiaDataCharacterResponse {
  character?: {
    character?: CharacterDetails;
  };
}

interface CharacterDetails {
  name?: string;
  level?: number;
  vocation?: string;
  world?: string;
  residence?: string;
  account_status?: string;
  guild?: {
    name?: string;
    rank?: string;
  };
}

@Component({
  imports: [FormsModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly http = inject(HttpClient);

  protected readonly characterName = signal('Trollefar');
  protected readonly character = signal<CharacterDetails | null>(null);
  protected readonly errorMessage = signal('');
  protected readonly isLoading = signal(false);

  protected readonly hasSearched = computed(() =>
    this.character() !== null || this.errorMessage().length > 0
  );

  protected searchCharacter(): void {
    const name = this.characterName().trim();

    if (!name) {
      this.character.set(null);
      this.errorMessage.set('Enter a character name.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');
    this.character.set(null);

    this.http
      .get<TibiaDataCharacterResponse>(
        `http://localhost:5267/character/${encodeURIComponent(name)}`
      )
      .pipe(finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: (response) => {
          const character = response.character?.character;

          if (!character?.name) {
            this.errorMessage.set('No character data was returned.');
            return;
          }

          this.character.set(character);
        },
        error: () => {
          this.errorMessage.set('Character lookup failed.');
        },
      });
  }

  protected updateCharacterName(value: string): void {
    this.characterName.set(value);
  }
}
