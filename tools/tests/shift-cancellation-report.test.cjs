const {test}=require('node:test');const assert=require('node:assert/strict');
const fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/shift-cancellation-report.js','utf8');
function setup(fetch){
    const elements={};function element(){return {children:[],listeners:{},value:'',dataset:{},hidden:false,
        append(child){this.children.push(child);if(child.selected)this.value=child.value;},replaceChildren(){this.children=[];},addEventListener(event,fn){this.listeners[event]=fn;}};}
    const document={getElementById(id){return elements[id]??=element();},createElement:element};
    document.getElementById('shift-cancellation-report').dataset.url='/report';
    vm.runInNewContext(source,{document,fetch,Intl,AbortController});return elements;
}
const flush=()=>new Promise(resolve=>setImmediate(resolve));
const reply=data=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>data});
const shifts=[{id:1,name:'Ca sáng',openedAt:'08/10/2026 08:00:00',status:'Closed'},{id:2,name:'Ca tối',openedAt:'08/10/2026 18:00:00',status:'Open'}];
test('complete report rows, both charge types and safe text rendering',async()=>{
    const base={batchId:5,batchNumber:1,sessionId:1,tableCode:'A01',orderItemId:10,itemName:'<script>x</script>',quantity:2,lineTotal:50000,actor:'Người huỷ',occurredAt:'08/10/2026 09:00:00',reason:'Khách đổi ý'};
    const elements=setup(async()=>reply({selectedShiftId:1,shifts,entries:[{...base,chargeWhenCancelled:false,chargedAmount:0},{...base,orderItemId:11,chargeWhenCancelled:true,chargedAmount:50000}]}));
    await flush();const rows=elements['report-rows'].children;assert.equal(rows.length,2);assert.equal(rows[0].children.length,9);
    assert.equal(rows[0].children[1].textContent,'#10 · <script>x</script>');
    assert.equal(rows[0].children[7].textContent,'Huỷ không tính tiền');assert.equal(rows[1].children[7].textContent,'Huỷ có tính tiền');
    assert.equal(elements['report-results'].hidden,false);assert.equal(elements['report-shift'].value,'1');
});
test('empty selected shift and no shifts have explanatory messages',async()=>{
    const empty=setup(async()=>reply({selectedShiftId:1,shifts,entries:[]}));await flush();
    assert.equal(empty['report-empty'].textContent,'Ca này không có nhật ký huỷ món.');
    const none=setup(async()=>reply({selectedShiftId:null,shifts:[],entries:[]}));await flush();
    assert.equal(none['report-empty'].textContent,'Chưa có ca phục vụ.');assert.equal(none['report-shift'].disabled,true);
});
test('changing shift and retry preserve selection, errors hide previous data',async()=>{
    const urls=[];let fail=false;
    const elements=setup(async url=>{urls.push(url);if(fail)throw new Error('offline');return reply({selectedShiftId:1,shifts,entries:[]});});
    await flush();elements['report-shift'].value='2';fail=true;elements['report-shift'].listeners.change();await flush();
    assert.equal(urls.at(-1),'/report?shiftId=2');assert.equal(elements['report-error'].hidden,false);assert.equal(elements['report-results'].hidden,true);
    fail=false;elements['report-reload'].listeners.click();await flush();assert.equal(urls.at(-1),'/report?shiftId=2');assert.equal(elements['report-error'].hidden,true);
});
test('old response cannot overwrite newly selected shift',async()=>{
    const requests=[];const elements=setup(()=>new Promise(resolve=>requests.push(resolve)));
    elements['report-shift'].value='2';elements['report-shift'].listeners.change();
    requests[1](reply({selectedShiftId:2,shifts,entries:[]}));await flush();
    requests[0](reply({selectedShiftId:1,shifts,entries:[]}));await flush();assert.equal(elements['report-shift'].value,'2');
});
