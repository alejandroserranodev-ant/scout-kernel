export interface Model {
  readonly version: string | null;
  readonly tagline: string | null;
}

export function ModelHero({ model }: { readonly model: Model }) {
  return <h2>{model.version || model.tagline}</h2>;
}
