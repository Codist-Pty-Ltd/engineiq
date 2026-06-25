"use client";

import { Suspense } from "react";
import { BillingView } from "./BillingView";

export default function BillingPage() {
  return (
    <Suspense fallback={<div className="eq-skeleton" style={{ height: 14, width: 200 }} />}>
      <BillingView />
    </Suspense>
  );
}
