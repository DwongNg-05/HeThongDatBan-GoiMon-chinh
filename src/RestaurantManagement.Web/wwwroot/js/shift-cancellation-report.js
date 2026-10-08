(() => {
    const root=document.getElementById('shift-cancellation-report');if(!root)return;
    const shift=document.getElementById('report-shift'),rows=document.getElementById('report-rows');
    const loading=document.getElementById('report-loading'),error=document.getElementById('report-error');
    const empty=document.getElementById('report-empty'),results=document.getElementById('report-results'),summary=document.getElementById('report-summary');
    let pending;
    const money=value=>new Intl.NumberFormat('vi-VN').format(value)+' ₫';
    async function load(id='') {
        pending?.abort();const request=new AbortController();pending=request;
        loading.hidden=false;error.hidden=empty.hidden=results.hidden=true;rows.replaceChildren();summary.textContent='';
        try {
            const response=await fetch(root.dataset.url+(id?`?shiftId=${encodeURIComponent(id)}`:''),{signal:request.signal,cache:'no-store',headers:{Accept:'application/json'}});
            if(!response.ok || !response.headers.get('content-type')?.includes('application/json'))throw new Error('report');
            const data=await response.json();if(pending!==request)return;
            shift.replaceChildren();shift.disabled=data.shifts.length===0;
            for(const item of data.shifts) {
                const option=document.createElement('option');option.value=String(item.id);
                option.textContent=`#${item.id} · ${item.name} · ${item.openedAt} · ${item.status==='Closed'?'Đã đóng':'Đang mở'}`;
                option.selected=item.id===data.selectedShiftId;shift.append(option);
            }
            for(const item of data.entries) {
                const row=document.createElement('tr');
                const values=[`Đơn #${item.batchId} (lần ${item.batchNumber}) · Phiên #${item.sessionId} · ${item.tableCode}`,
                    `#${item.orderItemId} · ${item.itemName}`,item.quantity,money(item.lineTotal),item.actor,item.occurredAt,
                    item.reason+(item.note?` · ${item.note}`:''),item.chargeWhenCancelled?'Huỷ có tính tiền':'Huỷ không tính tiền',money(item.chargedAmount)];
                for(const value of values){const cell=document.createElement('td');cell.textContent=value;row.append(cell);}rows.append(row);
            }
            empty.textContent=data.shifts.length===0?'Chưa có ca phục vụ.':'Ca này không có nhật ký huỷ món.';
            empty.hidden=data.entries.length!==0;results.hidden=data.entries.length===0;
            if(data.entries.length)summary.textContent=`${data.entries.length} lần huỷ · Không tính tiền: ${money(data.entries.filter(i=>!i.chargeWhenCancelled).reduce((s,i)=>s+i.lineTotal,0))} · Vẫn tính tiền: ${money(data.entries.reduce((s,i)=>s+i.chargedAmount,0))}`;
        }catch(failure){if(failure.name!=='AbortError' && pending===request)error.hidden=false;}
        finally{if(pending===request)loading.hidden=true;}
    }
    shift.addEventListener('change',()=>load(shift.value));
    document.getElementById('report-reload').addEventListener('click',()=>load(shift.value));
    load();
})();
