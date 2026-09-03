import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UploadTicket, Vehicle, VehicleRequest, VehicleStatusAction } from '../models/fleet.api';

/**
 * A dealer's own fleet (spec 4.3).
 *
 * The upload is three steps on purpose — ask for a URL, PUT the bytes, confirm — because that is the
 * shape of a real presigned upload. When storage moves to S3, step two becomes a PUT straight at the
 * bucket and nothing here changes.
 */
@Injectable({ providedIn: 'root' })
export class FleetService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/dealers/me/vehicles';

  readonly vehicles = httpResource<readonly Vehicle[]>(() => this.base);

  /** The car currently open in the form, or null when adding a new one. */
  readonly editing = signal<string | null>(null);

  readonly vehicle = httpResource<Vehicle>(() => {
    const id = this.editing();
    return id ? `${this.base}/${id}` : undefined;
  });

  async add(request: VehicleRequest): Promise<Vehicle> {
    return this.post<Vehicle>(this.base, request);
  }

  async update(vehicleId: string, request: VehicleRequest): Promise<Vehicle> {
    const token = await this.antiforgery();
    return firstValueFrom(
      this.http.put<Vehicle>(`${this.base}/${vehicleId}`, request, {
        headers: { 'X-XSRF-TOKEN': token },
      }),
    );
  }

  changeStatus(vehicleId: string, action: VehicleStatusAction): Promise<Vehicle> {
    return this.post<Vehicle>(`${this.base}/${vehicleId}/status`, { action });
  }

  async remove(vehicleId: string): Promise<void> {
    const token = await this.antiforgery();
    await firstValueFrom(
      this.http.delete(`${this.base}/${vehicleId}`, { headers: { 'X-XSRF-TOKEN': token } }),
    );
  }

  /**
   * The whole upload, start to finish. Kept in one method because the three steps are only
   * meaningful together: a ticket nobody PUTs to is litter, and bytes nobody confirms are invisible.
   */
  async uploadImage(vehicleId: string, file: File): Promise<Vehicle> {
    const ticket = await this.post<UploadTicket>(`${this.base}/${vehicleId}/images/upload-url`, {
      contentType: file.type,
    });

    const token = await this.antiforgery();
    await firstValueFrom(
      this.http.put(ticket.uploadUrl, file, {
        headers: { 'Content-Type': file.type, 'X-XSRF-TOKEN': token },
      }),
    );

    return this.post<Vehicle>(`${this.base}/${vehicleId}/images`, {
      storageKey: ticket.storageKey,
    });
  }

  async removeImage(vehicleId: string, imageId: string): Promise<Vehicle> {
    const token = await this.antiforgery();
    return firstValueFrom(
      this.http.delete<Vehicle>(`${this.base}/${vehicleId}/images/${imageId}`, {
        headers: { 'X-XSRF-TOKEN': token },
      }),
    );
  }

  setPrimaryImage(vehicleId: string, imageId: string): Promise<Vehicle> {
    return this.post<Vehicle>(`${this.base}/${vehicleId}/images/${imageId}/primary`, {});
  }

  refresh(): void {
    this.vehicles.reload();
    this.vehicle.reload();
  }

  private async post<T>(url: string, body: unknown): Promise<T> {
    const token = await this.antiforgery();
    return firstValueFrom(this.http.post<T>(url, body, { headers: { 'X-XSRF-TOKEN': token } }));
  }

  private async antiforgery(): Promise<string> {
    const response = await firstValueFrom(
      this.http.get<{ requestToken: string }>('/bff/antiforgery'),
    );
    return response.requestToken;
  }
}
